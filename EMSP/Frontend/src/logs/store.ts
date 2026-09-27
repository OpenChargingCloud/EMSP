import { api, ApiError, type LogEntry, type Permission } from '../api/client';

// The browser's copy of the EMSP's log, fed by two things: a snapshot from
// the JSON API and the Server-Sent Events stream. Both carry ids from the same
// counter on the EMSP, and the counter only ever grows - so an entry is
// taken only when it is newer than the snapshot, and a replayed stream (Hermod
// repeats its cached events to every new client) does no harm.
//
// The stream is not filtered: the snapshot and the events have to be about the
// same thing, or a filter changed at the browser would leave holes that only
// another round trip could fill. Filtering by tag and by level happens on this
// side, over what is already here, and is therefore instant.

export type StoreEvent =
    | { type: 'entries';  added: LogEntry[] }
    | { type: 'reloaded' }
    | { type: 'stream' }
    | { type: 'error';    text: string };

type Listener = (event: StoreEvent) => void;

/**
 * How many entries the page keeps. The EMSP keeps its own number and
 * answers with at most that many; this is the ceiling for a page that has been
 * open for a week.
 */
const MAX_ENTRIES = 5_000;

/** How much of the log a fresh page loads before it starts following along. */
const SNAPSHOT_SIZE = 1_000;

/**
 * How long after the stream has given up before the EMSP is asked why.
 *
 * Measured on the vehicle, whose store this is: it was stopped and started
 * again, which takes every session with it. The browser's own retry then got a
 * 401, which it treats as final and stops - and the page went on saying
 * "reconnecting ..." over a frozen list for as long as it was left open, with
 * the vehicle up and running and the operator signed out without being told.
 *
 * So when the browser gives up, the EMSP is asked why. A 401 is the answer,
 * and signs out through the same handler every other request uses. An answer
 * saying that this account may no longer read the log is the other refusal a
 * stream can meet here, and is said on the page. Anything else means the
 * stream was merely cut, and a new one is opened.
 */
const ASK_WHY_AFTER = 3_000;

/**
 * And how long before asking again, where the EMSP could not answer either.
 *
 * A pace that suits a page somebody has left open in front of an EMSP that
 * is being restarted, rather than one that hammers it.
 */
const ASK_AGAIN_AFTER = 10_000;

/**
 * What it takes to be sent the log: the event stream's own permission, and
 * the Logs page's. A driver is a customer, and the log is the operator's.
 */
const READS_THE_LOG: Permission = 'configuration:read';


export class LogStore {

    /** What is known, oldest first. */
    readonly entries: LogEntry[] = [];

    /** Every tag the EMSP has seen, for the filter. */
    readonly tags = new Set<string>();

    /** Whether the event stream is up. */
    streamConnected = false;

    /**
     * Whether the stream was refused for good: signed in, and no longer
     * somebody who may read the log. Then nothing is reconnecting, and the
     * page must not say so.
     */
    streamRefused = false;

    /** The newest id this page knows about. */
    lastId = 0;

    /** How many entries the EMSP keeps. */
    capacity = 0;

    private source:              EventSource | null = null;
    private readonly listeners = new Set<Listener>();

    /** Set while the EMSP is being asked why the stream stopped. */
    private askingWhy = false;

    /** The next attempt to find out, so that stopping cancels it. */
    private askAgain: ReturnType<typeof setTimeout> | null = null;


    onChange(listener: Listener): () => void {
        this.listeners.add(listener);
        return () => this.listeners.delete(listener);
    }


    /** Open the stream; every 'open' - the first and every reconnect - reloads. */
    start(): void {

        if (this.source !== null)
            return;

        const source        = new EventSource(api.eventsURL);
        this.source         = source;
        this.streamRefused  = false;

        source.addEventListener('open', () => {
            this.streamConnected = true;
            this.emit({ type: 'stream' });
            void this.reload();
        });

        source.addEventListener('error', () => {

            if (this.streamConnected) {
                this.streamConnected = false;
                this.emit({ type: 'stream' });
            }

            // CONNECTING means the browser will try again by itself, and the
            // page saying "reconnecting ..." is the truth. CLOSED means it has
            // given up - which is what a refused request looks like from here -
            // and then the page is claiming something that is not happening.
            if (source.readyState === EventSource.CLOSED)
                this.findOutWhy(source, ASK_WHY_AFTER);

        });

        source.addEventListener('log', event => {

            try
            {
                this.apply([JSON.parse((event as MessageEvent<string>).data) as LogEntry]);
            }
            catch (error)
            {
                console.warn('A log event could not be read:', error);
            }

        });

    }

    /**
     * Why the stream stopped, asked of the EMSP rather than guessed.
     *
     * The answer is worth having in all three directions. A 401 means the
     * session is gone and the sign-in page is where this person belongs. An
     * answer without the permission to read the log means the EMSP refused
     * the stream to somebody still signed in - an operator who is not one any
     * more - and a new stream would be refused again every few seconds, each
     * time with a warning in the very log it may not read; so it stops, and
     * the page says why. Anything else means the stream was cut rather than
     * refused, so a new one is opened. The browser would have opened it itself
     * had it not been given a status it takes as final.
     */
    private findOutWhy(Source: EventSource, In: number): void {

        if (this.askingWhy || this.source !== Source || this.askAgain !== null)
            return;

        this.askAgain = setTimeout(() => {

            this.askAgain = null;

            if (this.source !== Source)
                return;

            this.askingWhy = true;

            api.auth.me().then(
                me => {

                    this.askingWhy = false;

                    if (this.source !== Source)
                        return;

                    Source.close();
                    this.source = null;

                    if (!(me.permissions ?? []).includes(READS_THE_LOG)) {

                        this.streamRefused = true;
                        this.emit({ type: 'stream' });
                        this.emit({
                            type: 'error',
                            text: 'The log is not followed any more: the account signed in here may no longer read it.'
                        });

                        return;

                    }

                    this.start();

                },
                (problem: unknown) => {

                    this.askingWhy = false;

                    // Signed out: onUnauthorized has already been told, and
                    // what happens next is the router's business.
                    if (problem instanceof ApiError && problem.isUnauthorized)
                        return;

                    // The EMSP could not answer either, so it is still
                    // away. Keep trying, which is what the page is saying.
                    this.findOutWhy(Source, ASK_AGAIN_AFTER);

                }
            );

        }, In);

    }


    /** Close the stream and forget everything, e.g. at sign-out. */
    stop(): void {

        if (this.askAgain !== null) {
            clearTimeout(this.askAgain);
            this.askAgain = null;
        }

        this.source?.close();
        this.source = null;

        this.streamConnected = false;
        this.streamRefused   = false;
        this.lastId          = 0;

        this.entries.length = 0;
        this.tags.clear();

    }

    /** Throw away what this page shows, without touching the EMSP's log. */
    clear(): void {
        this.entries.length = 0;
        this.emit({ type: 'reloaded' });
    }


    /** The snapshot: what happened up to now, as far back as the EMSP keeps. */
    async reload(): Promise<void> {

        try
        {

            const page = await api.logs(SNAPSHOT_SIZE);

            this.entries.length = 0;
            this.entries.push(...page.entries);

            this.lastId    = Math.max(page.lastId, ...page.entries.map(entry => entry.id), 0);
            this.capacity  = page.capacity;

            for (const tag of page.tags)
                this.tags.add(tag);

            for (const entry of page.entries)
                for (const tag of entry.tags)
                    this.tags.add(tag);

            this.emit({ type: 'reloaded' });

        }
        catch (error)
        {
            this.emit({
                type: 'error',
                text: `Could not load the log: ${error instanceof Error ? error.message : String(error)}`
            });
        }

    }


    /** Everything the stream delivered that the snapshot did not already hold. */
    private apply(incoming: LogEntry[]): void {

        const added = incoming.filter(entry => entry.id > this.lastId).
                               sort((a, b) => a.id - b.id);

        if (added.length === 0)
            return;

        for (const entry of added) {

            this.entries.push(entry);
            this.lastId = entry.id;

            for (const tag of entry.tags)
                this.tags.add(tag);

        }

        if (this.entries.length > MAX_ENTRIES)
            this.entries.splice(0, this.entries.length - MAX_ENTRIES);

        this.emit({ type: 'entries', added });

    }

    private emit(event: StoreEvent): void {

        for (const listener of this.listeners)
        {
            try
            {
                listener(event);
            }
            catch (error)
            {
                console.error('A log listener failed:', error);
            }
        }

    }

}


export const logs = new LogStore();
