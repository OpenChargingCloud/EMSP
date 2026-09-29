/**
 * What the browser's copy of the log does when the stream stops.
 *
 * Run with `npm test`, which is Node's own runner reading the TypeScript as it
 * stands - no bundler, no browser, no dependency that is not already here.
 *
 * What is pinned is what was measured to be wrong on the vehicle, whose store
 * this is: it was stopped and started again, which takes every session with
 * it, and the browser's retry then got a 401 - which it treats as final. The
 * page went on saying "reconnecting ..." over a frozen list, with the vehicle
 * up and running and the operator signed out without being told. And what an
 * EMSP adds: a stream refused to somebody still signed in who may no longer
 * read the log, which is not asked for again every few seconds.
 */

import { strict as assert }       from 'node:assert';
import { registerHooks }          from 'node:module';
import { describe, it, mock }     from 'node:test';

// The pages are written for webpack, which does not want the extension in a
// relative import; Node does.
registerHooks({
    resolve(specifier, context, next) {
        return specifier.startsWith('.') && !specifier.endsWith('.ts')
                   ? next(`${specifier}.ts`, context)
                   : next(specifier, context);
    }
});

// The client reads @node/config, which reads <meta> tags when it is loaded.
(globalThis as unknown as { document: unknown }).document = { querySelector: () => null };


/** A stream that does what a test tells it to, and remembers being closed. */
class Stream {

    static readonly CONNECTING = 0;
    static readonly OPEN       = 1;
    static readonly CLOSED     = 2;

    static latest: Stream | null = null;

    readyState = Stream.CONNECTING;
    closed     = false;

    private readonly listeners = new Map<string, ((event: unknown) => void)[]>();

    readonly url: string;

    constructor(url: string) {
        this.url      = url;
        Stream.latest = this;
    }

    addEventListener(name: string, listener: (event: unknown) => void): void {
        this.listeners.set(name, [...(this.listeners.get(name) ?? []), listener]);
    }

    close(): void {
        this.closed     = true;
        this.readyState = Stream.CLOSED;
    }

    /** What the browser does to it, from the outside. */
    fire(name: string, event: unknown = {}): void {
        for (const listener of this.listeners.get(name) ?? [])
            listener(event);
    }

}

(globalThis as unknown as { EventSource: unknown }).EventSource = Stream;

const { LogStore } = await import('./store.ts');


/** What the EMSP was asked, and what it answered. */
let askedFor: string[] = [];

/** Who is signed in, in each of the two ways an EMSP can say yes. */
const operator  = { username: 'root',     roles: [ 'systemadmin' ], permissions: [ 'configuration:read' ] };
const customer  = { username: 'customer', roles: [ 'driver' ],      permissions: [ 'contracts:run' ] };

const emspAnswers = (How: 'yes' | 'yes, but not the log' | 'signed out' | 'not there') => {

    askedFor = [];

    (globalThis as unknown as { fetch: unknown }).fetch = (url: string) => {

        askedFor.push(url);

        if (How === 'not there')
            return Promise.reject(new TypeError('Failed to fetch'));

        const yes = How === 'yes' || How === 'yes, but not the log';

        return Promise.resolve({
            ok:          yes,
            status:      yes ? 200 : 401,
            statusText:  '',
            text:        () => Promise.resolve(How === 'yes'                  ? JSON.stringify(operator)
                                             : How === 'yes, but not the log' ? JSON.stringify(customer)
                                             :                                   JSON.stringify({ error: 'Not signed in.' }))
        } as unknown as Response);

    };

};

/** A store with its stream open and one that has just given up on it. */
const aStreamThatGaveUp = () => {

    const store = new LogStore();

    store.start();

    const stream = Stream.latest!;

    stream.readyState = Stream.CLOSED;
    stream.fire('error');

    return { store, stream };

};


describe('a stream that stops', () => {

    it('is left to the browser while the browser is still trying', async () => {

        mock.timers.enable({ apis: ['setTimeout'] });

        try
        {
            emspAnswers('yes');

            const store = new LogStore();
            store.start();

            // CONNECTING is the browser saying it will try again by itself,
            // and the page saying "reconnecting ..." is then the truth.
            Stream.latest!.readyState = Stream.CONNECTING;
            Stream.latest!.fire('error');

            mock.timers.tick(60_000);
            await Promise.resolve();

            assert.deepEqual(askedFor, [], 'the EMSP was asked about a stream nobody had given up on');

            store.stop();
        }
        finally
        {
            mock.timers.reset();
        }

    });

    it('makes the EMSP be asked why, once the browser has given up', async () => {

        mock.timers.enable({ apis: ['setTimeout'] });

        try
        {
            emspAnswers('yes');

            const { store } = aStreamThatGaveUp();

            assert.deepEqual(askedFor, [], 'the EMSP was asked before anything had settled');

            mock.timers.tick(5_000);
            await Promise.resolve();
            await Promise.resolve();

            assert.equal(askedFor.length, 1, 'nobody asked the EMSP anything');
            assert.match(askedFor[0]!, /auth\/me/);

            store.stop();
        }
        finally
        {
            mock.timers.reset();
        }

    });

    it('is opened again where the EMSP is there and the session still stands', async () => {

        mock.timers.enable({ apis: ['setTimeout'] });

        try
        {
            emspAnswers('yes');

            const { store, stream } = aStreamThatGaveUp();
            const first = stream;

            mock.timers.tick(5_000);
            for (let settle = 0; settle < 8; settle++)
                await Promise.resolve();

            assert.equal(first.closed, true, 'the stream that had stopped was left open');
            assert.notEqual(Stream.latest, first, 'no new stream was opened');

            store.stop();
        }
        finally
        {
            mock.timers.reset();
        }

    });

    it('is not opened again when the answer is that nobody is signed in', async () => {

        mock.timers.enable({ apis: ['setTimeout'] });

        try
        {
            emspAnswers('signed out');

            const { store, stream } = aStreamThatGaveUp();

            mock.timers.tick(5_000);
            for (let settle = 0; settle < 8; settle++)
                await Promise.resolve();

            // Signing out is what happens next, through the same handler every
            // other request uses; opening another stream would only get the
            // same refusal.
            assert.equal(Stream.latest, stream, 'a new stream was opened at an EMSP that had refused');

            // And it stops there rather than asking the same refused question
            // every ten seconds for as long as the page is open.
            const askedOnce = askedFor.length;

            mock.timers.tick(60_000);
            for (let settle = 0; settle < 8; settle++)
                await Promise.resolve();

            assert.equal(askedFor.length, askedOnce,
                         'it went on asking an EMSP that had already said no');

            store.stop();
        }
        finally
        {
            mock.timers.reset();
        }

    });

    it('keeps trying where the EMSP could not answer either', async () => {

        mock.timers.enable({ apis: ['setTimeout'] });

        try
        {
            emspAnswers('not there');

            const { store } = aStreamThatGaveUp();

            mock.timers.tick(5_000);
            for (let settle = 0; settle < 8; settle++)
                await Promise.resolve();

            assert.equal(askedFor.length, 1);

            // An EMSP that is being restarted is away for longer than one
            // attempt, and a page left open in front of it is expected to come
            // back on its own.
            mock.timers.tick(15_000);
            for (let settle = 0; settle < 8; settle++)
                await Promise.resolve();

            assert.ok(askedFor.length > 1, 'it gave up after one try and went on saying otherwise');

            store.stop();
        }
        finally
        {
            mock.timers.reset();
        }

    });

    it('is not opened again for somebody still signed in who may no longer read the log', async () => {

        mock.timers.enable({ apis: ['setTimeout'] });

        try
        {
            emspAnswers('yes, but not the log');

            const { store, stream } = aStreamThatGaveUp();

            const said: string[] = [];
            store.onChange(event => { if (event.type === 'error') said.push(event.text); });

            mock.timers.tick(5_000);
            for (let settle = 0; settle < 8; settle++)
                await Promise.resolve();

            // A new stream would only be refused again - by an EMSP that
            // writes a warning into its log for every refusal.
            assert.equal(stream.closed,  true,   'the stream that had been refused was left open');
            assert.equal(Stream.latest,  stream, 'a new stream was opened for somebody who may not read the log');
            assert.equal(store.streamRefused, true, 'the page would go on saying "reconnecting ..."');
            assert.equal(said.length,    1,      'the page was not told why');

            // And it is not asked about again either.
            const askedOnce = askedFor.length;

            mock.timers.tick(60_000);
            for (let settle = 0; settle < 8; settle++)
                await Promise.resolve();

            assert.equal(askedFor.length, askedOnce, 'it went on asking about a stream that had been refused');

            store.stop();

            assert.equal(store.streamRefused, false, 'a sign-out left the refusal behind for the next account');
        }
        finally
        {
            mock.timers.reset();
        }

    });

    it('and stops being asked about once the page is done with it', async () => {

        mock.timers.enable({ apis: ['setTimeout'] });

        try
        {
            emspAnswers('not there');

            const { store } = aStreamThatGaveUp();

            store.stop();

            mock.timers.tick(60_000);
            for (let settle = 0; settle < 8; settle++)
                await Promise.resolve();

            assert.deepEqual(askedFor, [], 'a store that had been stopped went on asking');
        }
        finally
        {
            mock.timers.reset();
        }

    });

});
