import { config } from '../config';


// What the JSON API answers. Everything below /api/v1 except the sign-in needs
// the session cookie, which the browser sends by itself because every request
// here is same-origin.

/** How loudly a log entry asks to be read. */
export type LogLevel = 'debug' | 'info' | 'notice' | 'warning' | 'error' | 'critical';

/** The levels in the order the EMSP defines them, quietest first. */
export const logLevels: LogLevel[] = ['debug', 'info', 'notice', 'warning', 'error', 'critical'];

/** One thing that happened inside the EMSP. */
export interface LogEntry {
    /** A number that only ever grows, so the page can tell what it has seen. */
    id:         number;
    timestamp:  string;
    level:      LogLevel;
    /** What it is about: "ocpi", "partner", "http", ... - without the level. */
    tags:       string[];
    message:    string;
    /** Whatever else belongs to it, when there is more than one line to say. */
    data?:      unknown;
}

/** What a page of the log brings back. */
export interface LogPage {
    /** The newest id of the whole log, whatever this page was filtered by. */
    lastId:    number;
    capacity:  number;
    tags:      string[];
    entries:   LogEntry[];
}

/**
 * What somebody signed in to this EMSP may do.
 *
 * A copy of what the EMSP enforces, not the enforcement: it is here so a page
 * can grey out what this person may not do instead of offering it and letting
 * them find out by being refused. Every request is checked again on arrival,
 * so editing this list in a browser buys a button that answers 403.
 */
export type Permission = 'readConfiguration'
                       | 'changeNetworkSettings'
                       | 'runDiagnostics'
                       | 'manageTokens'
                       | 'manageRoamingPartners'
                       | 'issueContracts'
                       | 'manageContracts';

/** Who is signed in to the web interface. */
export interface Me {
    username:     string;
    roles:        string[];
    permissions:  Permission[];
}

/** How the EMSP is doing right now. */
export interface Status {
    service:    string;
    version:    string;
    partyId:    string;
    hermod:     string | null;
    timestamp:  string;
    startedAt:  string;
    uptime:     string;
    sessions:   number;
    log:        { entries: number; capacity: number; lastId: number; tags: string[] };
}

/**
 * What the EMSP is made of. Only the shape the Configuration page relies on
 * is named; the rest is rendered from whatever the EMSP sends, so that a new
 * section on the server needs no change here.
 */
export interface Configuration {
    EMSP:        Record<string, unknown>;
    http:        Record<string, unknown>;
    web:         Record<string, unknown>;
    log:         Record<string, unknown>;
    time:        Record<string, unknown>;
    ocpi:        Record<string, unknown>;
    assemblies:  Record<string, unknown>[];
}


/** One name server this EMSP asks. */
export interface DNSServer {
    /** An IP address or a host name. */
    address:              string;
    port:                 number;
    transport:            string;
    queryTimeoutSeconds:  number | null;
}

/** What may be changed about the name resolution while the EMSP runs. */
export interface DNSSettings {
    queryTimeoutSeconds:  number;
    /** null leaves it to the server's own default. */
    recursionDesired:     boolean | null;
    useCache:             boolean;
    dnssecOK:             boolean;
    followCNAMEs:         boolean;
    maxCNAMEFollows:      number;
    maxRetries:           number;
}

/** How this EMSP resolves names. */
export interface DNSConfiguration {
    enabled:    boolean;
    servers:    DNSServer[];
    settings:   DNSSettings;
    /** What was decided when the client was made, and is not on offer. */
    fixed:      Record<string, unknown>;
    limits: {
        maxServers:       number;
        maxQueryTimeout:  number;
        transports:       string[];
        recordTypes:      string[];
    };
    file:       string;
}

/** What a PUT to the DNS configuration may carry; everything is optional. */
export interface DNSUpdate {
    enabled?:              boolean;
    servers?:              DNSServer[];
    queryTimeoutSeconds?:  number;
    recursionDesired?:     boolean | null;
    useCache?:             boolean;
    dnssecOK?:             boolean;
    followCNAMEs?:         boolean;
    maxCNAMEFollows?:      number;
    maxRetries?:           number;
}

/** One resource record a test query brought back. */
export interface DNSRecord {
    name:        string;
    type:        string;
    timeToLive:  number;
    value:       string;
}

/** What a test query brought back. */
export interface DNSQueryResult {
    name:           string;
    recordTypes:    string[];
    ok:             boolean;
    error?:         string;
    responseCode?:  string;
    server?:        string;
    runtime_ms?:    number;
    authoritative?: boolean;
    truncated?:     boolean;
    dnssec?:        string | null;
    timedOut?:      boolean;
    answers:        DNSRecord[];
    more?:          number;
}


/** One line of what happened while a time server was being asked. */
export interface TimeServerTestStep {
    at_ms:  number;
    level:  'info' | 'notice' | 'warning' | 'error';
    text:   string;
}

/** What came of asking one time server everything. */
export interface TimeServerTest {
    host:        string;
    ok:          boolean;
    runtime_ms:  number;
    steps:       TimeServerTestStep[];
}

/**
 * What may be changed about the time servers while the EMSP runs. What is
 * left out stays as it is; the list of servers is one value and replaces the
 * EMSP's whole.
 */
export interface NTSUpdate {
    enabled?:              boolean;
    servers?:              NTSServerEntry[];
    minServers?:           number;
    maxDeviationSeconds?:  number;
    checkEverySeconds?:    number;
    timeoutSeconds?:       number;
}

/**
 * One time server as the configuration names it. Whatever is left out is the
 * usual: priority 0, the usual ports, switched on.
 */
export interface NTSServerEntry {
    hostname:    string;
    priority?:   number;
    ntsKEPort?:  number;
    ntpPort?:    number;
    enabled?:    boolean;
}

/** How one synchronisation went, step by step. */
export interface NTSSyncResult {
    ok:           boolean;
    server:       string;
    at:           string;
    error?:       string;
    step?:        string;
    runtime_ms?:  number;
    offset_ms?:   number | null;

    /** What the group concluded: the median, how many answered, how far apart. */
    group?:       {
        name:               string;
        answered:           number;
        required:           number;
        offset_ms:          number | null;
        spread_ms:          number | null;
        deviationExceeded:  boolean;
    };

    /** One entry per server asked, answered or not. */
    servers?:     NTSServerResult[];

    /** Only from the detailed test of a single server. */
    ntske?:       Record<string, unknown>;
    ntp?:         Record<string, unknown>;
}

/** What one time server of a group said. */
export interface NTSServerResult {
    hostname:       string;
    ok:             boolean;
    offset_ms?:     number | null;
    roundTrip_ms?:  number | null;
    authenticated?: boolean | null;
    keyExchange?:   string;
    error?:         string | null;
}

/** One server of this EMSP's group, and what its key exchange is doing. */
export interface NTSTimeSource {
    hostname:       string;
    priority:       number;
    ntsKEPort:      number;
    ntpPort:        number;
    enabled:        boolean;
    cookies?:       number | null;
    lastExchange?:  string | null;
    aeadAlgorithm?: string | null;
}

/** Where this EMSP gets the time from, and how its key exchange is doing. */
export interface NTSConfiguration {
    enabled:   boolean;

    /**
     * Every server this EMSP has, switched on or not, in the order they
     * were configured - and the rules for believing them.
     */
    timeSources?:  NTSTimeSource[];
    group?:        { name: string; minServers: number; maxDeviationSeconds: number };

    /**
     * What may be changed about the group and the test. The quorum is the one
     * wanted; the group's own can be lower while it has fewer servers on.
     */
    settings:  {
        timeoutSeconds:       number | null;
        checkEverySeconds:    number;
        minServers:           number;
        maxDeviationSeconds:  number;
    };
    /** What any new client starts with, the group's and the test's alike. */
    policy:    Record<string, unknown>;
    lastSync:  NTSSyncResult | null;
    limits:    {
        maxTimeout:        number;
        minCheckEvery:     number;
        maxCheckEvery:     number;
        minDeviation:      number;
        maxDeviation:      number;
        defaultNTSKEPort:  number;
        defaultNTPPort:    number;
    };
    file:      string;
    /** Only on the answer to a synchronisation, which carries both. */
    result?:   NTSSyncResult;
}

/**
 * What time it is here, and what that is worth. `now` is the EMSP's own
 * system clock, and everything under `nts` is what happened when it last asked
 * a server that knows. `legal` is decided by the EMSP and never by this page.
 */
export interface Clock {
    now:        string;
    source:     string;
    nts: {
        enabled:       boolean;
        /** The group the clock is checked against, its servers switched on and its quorum; null while switched off. */
        group:         string   | null;
        servers:       string[] | null;
        minServers:    number   | null;
        lastServer:    string | null;
        checkedAt:     string | null;
        ageSeconds:    number | null;
        offset_ms:     number | null;
        everySeconds:  number;
    };
    legal:            boolean;
    authority:        string | null;
    why:              string | null;
    toleranceSeconds: number;
    maxAgeSeconds:    number;
}


// OCPI

/** Where one OCPI version is: its endpoints as absolute URLs a partner is told. */
export interface OCPIVersionEndpoints {
    version:      string;
    details:      string;
    credentials:  string;
    modules:      Record<string, string>;
}

/** The OCPI side of this EMSP: who it is, where it is, how much it holds. */
export interface OCPIConfiguration {
    party: {
        countryCode:  string;
        partyId:      string;
        id:           string;
        role:         string;
        name:         string;
        website:      string | null;
    };
    endpoints: {
        base:         string;
        versions:     string;
        externalURL:  string | null;
        byVersion:    OCPIVersionEndpoints[];
    };
    versions:       string[];
    knownVersions:  string[];
    settings: {
        locationsAsOpenData:  boolean;
        tariffsAsOpenData:    boolean;
        allowDowngrades:      boolean;
        logRequests:          boolean;
        logPayloads:          boolean;
    };
    counts: {
        partners:   number;
        tokens:     number;
        locations:  number;
        tariffs:    number;
        sessions:   number;
        cdrs:       number;
    };
    directory:  string;
    file:       string;
}

/**
 * One roaming partner. The tokens are present only for whoever may manage
 * partners; everybody else sees that there is one.
 */
export interface Partner {
    version:           string;
    id:                string;
    countryCode:       string;
    partyId:           string;
    role:              string;
    name:              string;
    website:           string | null;
    status:            string;
    ourToken:          string | null;
    hasOurToken:       boolean;
    ourTokenStatus:    string | null;
    theirToken:        string | null;
    hasTheirToken:     boolean;
    theirVersionsURL:  string | null;
    remoteStatus:      string | null;
    selectedVersion:   string | null;
    /** Whether this EMSP holds a token of theirs and a place to send it. */
    canRegister:       boolean;
    /** Whether the peering is complete in both directions. */
    registered:        boolean;
    created:           string;
    lastUpdated:       string;
}

/** Every roaming partner, and what the add form may choose from. */
export interface Partners {
    partners:        Partner[];
    versions:        string[];
    roles:           string[];
    ourVersionsURL:  string;
}

/** What it takes to add a roaming partner. */
export interface PartnerSpec {
    version:       string;
    countryCode:   string;
    partyId:       string;
    role:          string;
    name:          string;
    website?:      string;
    /** Empty means "make one up"; it comes back in the answer. */
    ourToken?:     string;
    /** Both or neither: with both, this EMSP can start the peering itself. */
    theirToken?:   string;
    versionsURL?:  string;
}

/** One token this EMSP issued, as OCPI writes it, with the version and the status added. */
export interface Token {
    version:         string;
    status:          string;
    uid:             string;
    type:            string;
    /** OCPI 2.2 and later. */
    contract_id?:    string;
    /** OCPI 2.1.1. */
    auth_id?:        string;
    issuer:          string;
    valid:           boolean;
    whitelist:       string;
    visual_number?:  string;
    language?:       string;
    last_updated:    string;
    [other: string]: unknown;
}

/** Every token, and what the form may choose from. */
export interface Tokens {
    tokens:      Token[];
    versions:    string[];
    types:       string[];
    whitelists:  string[];
    issuer:      string;
    partyId:     string;
}

/** What it takes to issue a token. */
export interface TokenSpec {
    version:        string;
    uid:            string;
    type:           string;
    contractId?:    string;
    issuer?:        string;
    valid?:         boolean;
    whitelist?:     string;
    visualNumber?:  string;
    language?:      string;
}

/** What the partners pushed: locations, tariffs, sessions or charge detail records. */
export type RoamingDataKind = 'locations' | 'tariffs' | 'sessions' | 'cdrs';

/** One object as OCPI writes it, with the version added. */
export type RoamingItem = { version: string } & Record<string, unknown>;

export interface RoamingData {
    kind:      RoamingDataKind;
    versions:  string[];
    items:     RoamingItem[];
}


// Contracts

/** Where a contract stands. */
export type ContractStatus = 'valid' | 'revoked' | 'expired' | 'pending';

/** One contract certificate this EMSP issued. */
export interface Contract {
    /** As people read it: "DE-GDF-C12345678-X". */
    emaId:         string;
    /** As the certificate carries it: "DEGDFC12345678X". */
    emaIdCompact:  string;
    owner:         string;
    serialNumber:  string;
    thumbprint:    string;
    notBefore:     string;
    notAfter:      string;
    issuedAt:      string;
    revokedAt:     string | null;
    revokedBy:     string | null;
    status:        ContractStatus;
    file:          string;
    /** The certificate as PEM, when it could be read. */
    certificate?:  string;
}

/** The mobility operator root every contract chains up to. */
export interface MORoot {
    subject:      string;
    fingerprint:  string;
    notAfter:     string;
    pem:          string;
    file:         string;
}

/** The contracts as the page reads them: one account's, or everybody's. */
export interface Contracts {
    party:         string;
    issuer:        string;
    validityDays:  number;
    signUp:        boolean;
    /** Whether this is every contract this EMSP issued, or only one's own. */
    everyone:      boolean;
    moRoot:        MORoot;
    /** The two sub-CAs as PEM, the signer first: what a certificate is bundled with. */
    chain:         string;
    directory:     string;
    contracts:     Contract[];
}

/** What comes back when a contract was issued. */
export interface ContractIssued {
    message:      string;
    contract:     Contract;
    certificate:  string;
    chain:        string;
    moRoot:       string;
    contracts:    Contracts;
}


export class ApiError extends Error {

    constructor(public readonly status:  number,
                message:                 string,
                public readonly body?:   unknown) {
        super(message);
        this.name = 'ApiError';
    }

    get isUnauthorized(): boolean {
        return this.status === 401;
    }

}


let unauthorizedHandler: (() => void) | null = null;

/** Called whenever the API answers 401, i.e. the session is gone. */
export function onUnauthorized(handler: () => void): void {
    unauthorizedHandler = handler;
}


/**
 * Sign in at the HTTPExt API and answer with who is now signed in.
 *
 * Two requests rather than one: the HTTPExt API is the only place that can
 * check a password, but it knows nothing of this EMSP's roles. So it sets the
 * session cookie, and "me" is asked afterwards for the roles and permissions
 * this frontend actually works from.
 */
async function signIn(username: string, password: string): Promise<Me> {

    const response = await fetch(config.extBase + '/login', {
                               method:       'POST',
                               headers:      {
                                                 'Content-Type':  'application/x-www-form-urlencoded',
                                                 'Accept':        'application/json'
                                             },
                               credentials:  'same-origin',
                               body:         new URLSearchParams({ login: username, password }).toString()
                           });

    if (!response.ok) {

        let message = `${response.status} ${response.statusText}`;

        try {
            const json = JSON.parse(await response.text());
            if (typeof json === 'object' && json !== null) {
                if      ('description' in json && typeof json.description === 'string')  message = json.description;
                else if ('error'       in json && typeof json.error       === 'string')  message = json.error;
            }
        }
        catch { /* the status line says enough */ }

        throw new ApiError(response.status, message, null);

    }

    return request<Me>('GET', '/auth/me');

}


/**
 * Sign up at the HTTPExt API's own sign-up - Hermod's opt-in, which the EMSP
 * attaches when its configuration allows it - and answer with who is now
 * signed in: the sign-up hands out the session itself, and the EMSP puts the
 * account into the driver group before it does.
 */
async function signUp(username:     string,
                      email:        string,
                      password:     string,
                      displayName?: string): Promise<Me> {

    const response = await fetch(config.extBase + '/auth/signup', {
                               method:       'POST',
                               headers:      {
                                                 'Content-Type':  'application/json',
                                                 'Accept':        'application/json'
                                             },
                               credentials:  'same-origin',
                               body:         JSON.stringify({
                                                 username,
                                                 email,
                                                 password,
                                                 displayName: displayName || undefined
                                             })
                           });

    if (!response.ok) {

        let message = response.status === 404
                          ? 'Signing up is switched off at this EMSP.'
                          : `${response.status} ${response.statusText}`;

        try {
            const json = JSON.parse(await response.text());
            if (typeof json === 'object' && json !== null && 'description' in json && typeof json.description === 'string')
                message = json.description;
        }
        catch { /* the status line says enough */ }

        throw new ApiError(response.status, message, null);

    }

    await response.arrayBuffer();

    return request<Me>('GET', '/auth/me');

}


async function request<T>(method: string, path: string, body?: unknown): Promise<T> {

    const headers: Record<string, string> = { 'Accept': 'application/json' };

    if (body !== undefined)
        headers['Content-Type'] = 'application/json';

    const response = await fetch(config.apiBase + path, {
                               method,
                               headers,
                               credentials: 'same-origin',
                               body: body !== undefined ? JSON.stringify(body) : undefined
                           });

    if (response.status === 401)
        unauthorizedHandler?.();

    if (response.status === 204) {
        await response.arrayBuffer();
        return undefined as T;
    }

    const text = await response.text();
    let json: unknown = null;

    try {
        json = text.length > 0 ? JSON.parse(text) : null;
    }
    catch {
        if (response.ok)
            throw new ApiError(response.status, `Invalid JSON in the response of ${method} ${path}`, text);
    }

    if (!response.ok) {

        const message = typeof json === 'object' && json !== null
                            ? 'error'   in json && typeof json.error   === 'string' ? json.error
                            : 'message' in json && typeof json.message === 'string' ? json.message
                            : `${response.status} ${response.statusText}`
                            : `${response.status} ${response.statusText}`;

        throw new ApiError(response.status, message, json);

    }

    return json as T;

}


export const api = {

    /** The Server-Sent Events stream; the browser sends the session cookie along. */
    eventsURL: `${config.apiBase}/events`,

    auth: {
        me:      ()                                    => request<Me>  ('GET',  '/auth/me'),
        login:   signIn,
        signUp,
        logout:  ()                                    => request<void>('POST', '/auth/logout')
    },

    /** The contract certificates: one's own, or everybody's for the operator. */
    contracts: {

        get:      ()              => request<Contracts>('GET', '/contracts'),

        /** A contract for the key in the request; the answer carries the certificate, the sub-CAs and the MO root. */
        issue:    (csr: string)   => request<ContractIssued>('POST', '/contracts', { csr }),

        revoke:   (emaId: string) => request<{ message: string; contract: Contract; contracts: Contracts }>(
                                         'POST', `/contracts/${encodeURIComponent(emaId)}/revoke`, {}),

        /** Where the MO root is fetched as a file, for whoever prefers a curl to a button. */
        moRootURL: `${config.apiBase}/contracts/mo-root.pem`

    },

    status:         () => request<Status>       ('GET', '/status'),
    configuration:  () => request<Configuration>('GET', '/configuration'),

    /** What time it is here and what that is worth; cheap, and safe to poll. */
    clock:          () => request<Clock>        ('GET', '/configuration/time'),

    dns: {
        get:   ()                    => request<DNSConfiguration>('GET', '/configuration/dns'),
        save:  (update: DNSUpdate)   => request<DNSConfiguration>('PUT', '/configuration/dns', update),
        query: (name: string, recordTypes: string[]) =>
                   request<DNSQueryResult>('POST', '/configuration/dns/query', { name, recordTypes })
    },

    nts: {
        get:   ()                    => request<NTSConfiguration>('GET', '/configuration/nts'),
        save:  (update: NTSUpdate)   => request<NTSConfiguration>('PUT', '/configuration/nts', update),
        /**
         * Ask one time server everything: the name, the key exchange and what
         * the certificate claims, the authenticated NTP request, each one
         * written down as it happens.
         *
         * @param host  which server, on the ports it is configured with.
         */
        test:  (host: string)        => request<TimeServerTest>('POST', '/configuration/nts/test', { host }),
        /** Ask every server of the group, with every step in the log. */
        sync:  ()                    => request<NTSConfiguration>('POST', '/configuration/nts/sync', {})
    },

    /** The OCPI side: who this EMSP is, its partners, its tokens, and what the partners pushed. */
    ocpi: {

        configuration: () => request<OCPIConfiguration>('GET', '/configuration/ocpi'),

        partners: {

            get:       ()                     => request<Partners>('GET', '/ocpi/partners'),

            /**
             * Add a partner. The answer carries the token this EMSP made up
             * for them - the one thing that has to be handed over by hand.
             */
            add:       (spec: PartnerSpec)    => request<{ message: string; id: string; version: string; ourToken: string; partners: Partners }>(
                                                     'POST', '/ocpi/partners', spec),

            /** Start the peering from here: fetch their versions, POST our credentials. */
            register:  (version: string, id: string) =>
                           request<{ ok: boolean; message: string; partners: Partners }>(
                               'POST', `/ocpi/partners/${encodeURIComponent(version)}/${encodeURIComponent(id)}/register`, {}),

            remove:    (version: string, id: string) =>
                           request<Partners>('DELETE', `/ocpi/partners/${encodeURIComponent(version)}/${encodeURIComponent(id)}`)

        },

        tokens: {

            get:     ()                 => request<Tokens>('GET', '/ocpi/tokens'),

            add:     (spec: TokenSpec)  => request<{ message: string; uid: string; version: string; tokens: Tokens }>(
                                               'POST', '/ocpi/tokens', spec),

            remove:  (version: string, uid: string) =>
                         request<Tokens>('DELETE', `/ocpi/tokens/${encodeURIComponent(version)}/${encodeURIComponent(uid)}`)

        },

        /** What the partners pushed, of one kind, over every version. */
        data: (kind: RoamingDataKind) => request<RoamingData>('GET', `/ocpi/${kind}`)

    },

    /**
     * A page of the log, oldest of the returned entries first.
     *
     * @param limit  at most this many entries
     * @param after  only what is newer than this id
     * @param tag    only entries carrying this tag - a level counting as one
     */
    logs: (limit?: number, after?: number, tag?: string) => {

        const query = new URLSearchParams();

        if (limit !== undefined)  query.set('limit', String(limit));
        if (after !== undefined)  query.set('after', String(after));
        if (tag)                  query.set('tag',   tag);

        const suffix = query.size > 0 ? `?${query}` : '';

        return request<LogPage>('GET', `/logs${suffix}`);

    }

};
