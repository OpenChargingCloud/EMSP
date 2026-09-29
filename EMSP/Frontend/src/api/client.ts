import { config } from '@node/config';
import { ApiError, NoAnswer, actWithin, apiURL, nodeAPI, request,
         type Certificate as NodeCertificate, type CertificateImport as NodeCertificateImport,
         type CertificateStore as NodeCertificateStore, type NodeConfiguration, type NodeMe,
         type NodeResource, type NodeStatus, type Permission as NodePermission } from '@node/api/client';


// What the JSON API of an EMSP answers. Everything every node answers is
// WWCP_Node's, in @node/api/client - the requests and their deadlines, what a
// page is told when no answer comes, the sign-in, and the routes and types of
// the JSON API every node has - and is handed on from here, so that a page asks
// one client. What only an EMSP says is on top of it: who may touch its OCPI
// and its contracts, what its status and configuration add, the kinds its
// certificate store keeps, the sign-up, and its own routes.

export * from '@node/api/client';


/**
 * What a role may be allowed to touch on this EMSP: what every node has, and
 * what an EMSP adds to it.
 */
export type Resource = NodeResource | 'ocpi' | 'partners' | 'tokens' | 'contracts';

/**
 * What somebody signed in to this EMSP may do: an operation on a resource,
 * written "dns:edit" - on the node's resources and on the EMSP's own.
 */
export type Permission = NodePermission<Resource>;

/** Who is signed in to the web interface. */
export type Me = NodeMe<Resource>;

/** How the EMSP is doing right now: what every node says, and who it is in OCPI. */
export interface Status extends NodeStatus {
    partyId:  string;
}

/**
 * What the EMSP is made of: the node's sections, and its own. Only the shape
 * the Configuration page relies on is named; the rest is rendered from
 * whatever the EMSP sends, so that a new section on the server needs no change
 * here.
 */
export interface Configuration extends NodeConfiguration {
    EMSP:        Record<string, unknown>;
    ocpi:        Record<string, unknown>;
    assemblies:  Record<string, unknown>[];
}


// The certificate store

/**
 * What a certificate is for. Roots are believed and the EMSP's own
 * certificate is presented; a server certificate is neither, but kept to
 * recognise a server by its fingerprint.
 *
 * The seven kinds this EMSP keeps. The node knows four more - a vehicle's
 * own certificate, its contracts, its provisioning certificate and the one it
 * checks a tariff with - which only a vehicle holds, and which the store here
 * refuses.
 */
export type CertificateKind = 'v2gRoot' | 'moRoot' | 'oemRoot' | 'tlsRoot' | 'clientRoot'
                            | 'tlsServer' | 'tlsIdentity';

/** One certificate in the store, of one of the EMSP's kinds. */
export type Certificate        = NodeCertificate<CertificateKind>;

/** The whole store, grouped the way it is shown, of the EMSP's kinds. */
export type CertificateStore   = NodeCertificateStore<CertificateKind>;

/** What an import sends: the file, base64-encoded, and which of the EMSP's kinds it is. */
export type CertificateImport  = NodeCertificateImport<CertificateKind>;


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

    const giveUp = new AbortController();
    const timer  = setTimeout(() => giveUp.abort(), actWithin);

    let response: Response;

    try
    {
        response = await fetch(config.extBase + '/auth/signup', {
                             method:       'POST',
                             headers:      {
                                               'Content-Type':  'application/json',
                                               'Accept':        'application/json'
                                           },
                             credentials:  'same-origin',
                             signal:       giveUp.signal,
                             body:         JSON.stringify({
                                               username,
                                               email,
                                               password,
                                               displayName: displayName || undefined
                                           })
                         });
    }
    catch (problem)
    {
        throw nothingCameBack(problem, 'POST', actWithin, giveUp.signal.aborted);
    }
    finally
    {
        clearTimeout(timer);
    }

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


/**
 * What to say when nothing came back, in words somebody can act on - for the
 * sign-up, which goes to the HTTPExt API rather than through request(). A copy
 * of the node's, which @node/api/client keeps to itself, until it hands out one
 * for a kind's own requests.
 *
 * A read that runs out of time changed nothing, and can be told so. A write
 * that runs out of time is the honest awkward case: the page stopped waiting,
 * but the EMSP may well have done the thing and been slow to say so, and
 * telling somebody that it did not work would invite them to do it twice. So
 * it says what is actually known - that the waiting stopped - and where to
 * look for the rest.
 */
function nothingCameBack(Problem:  unknown,
                         Method:   string,
                         Within:   number,
                         GaveUp:   boolean): unknown {

    const seconds = Math.round(Within / 1000);

    if (GaveUp)
        return new NoAnswer(
                   'ran out of time',
                   Method === 'GET'
                       ? `The EMSP did not answer within ${seconds} seconds. ` +
                         'It may be busy, restarting, or no longer reachable from here.'
                       : `The EMSP did not answer within ${seconds} seconds, so this page ` +
                         'stopped waiting. It may still have carried this out - reload to see ' +
                         'what it now says.'
               );

    // The browser's own word for this is "Failed to fetch", which on a page
    // about an EMSP names neither the EMSP nor what to do next.
    if (Problem instanceof TypeError)
        return new NoAnswer(
                   'could not be reached',
                   'The EMSP could not be reached. It may be switched off, restarting, ' +
                   'or on the other side of a network that is down.'
               );

    return Problem;

}


/** The routes every node has, typed with what an EMSP says its own of them are. */
const node = nodeAPI<{
    me:             Me;
    status:         Status;
    configuration:  Configuration;
    kind:           CertificateKind;
    store:          CertificateStore;
}>();


export const api = {

    ...node,

    auth: {
        ...node.auth,
        signUp
    },

    /** The contract certificates: one's own, or everybody's for the operator. */
    contracts: {

        get:      ()              => request<Contracts>('GET', '/contracts'),

        /** A contract for the key in the request; the answer carries the certificate, the sub-CAs and the MO root. */
        issue:    (csr: string)   => request<ContractIssued>('POST', '/contracts', { csr }),

        revoke:   (emaId: string) => request<{ message: string; contract: Contract; contracts: Contracts }>(
                                         'POST', `/contracts/${encodeURIComponent(emaId)}/revoke`, {}),

        /** Where the MO root is fetched as a file, for whoever prefers a curl to a button. */
        moRootURL: apiURL('/contracts/mo-root.pem')

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

    }

};
