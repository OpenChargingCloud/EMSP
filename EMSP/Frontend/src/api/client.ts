import { ApiError, apiURL, extRequest, nodeAPI, request,
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
 * the Configuration page relies on is named; the fields of each section are
 * rendered from whatever the EMSP sends, and which sections there are is the
 * page's to say.
 */
export interface Configuration extends NodeConfiguration {
    EMSP:        Record<string, unknown>;
    ocpi:        Record<string, unknown>;
    contracts:   Record<string, unknown>;
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


// The drivers

/** Where a driver's RFID card stands: waiting, charging, blocked, or turned down. */
export type CardState = 'requested' | 'active' | 'blocked' | 'rejected';

/** An RFID card a driver brought. */
export interface Card {
    /** As the card sends it: hex digits, upper case, nothing between them. */
    uid:          string;
    owner:        string;
    label:        string | null;
    state:        CardState;
    requestedAt:  string;
    decidedAt:    string | null;
    decidedBy:    string | null;
    /** Why the operator turned it down. */
    reason:       string | null;
    /** The contract its token is made out to, once it was let in. */
    contractId:   string | null;
    changedAt:    string | null;
    changedBy:    string | null;
}

/** The cards as the page reads them: one account's, or every driver's. */
export interface Cards {
    /** Whether this is every driver's card, or only one's own. */
    everyone:  boolean;
    issuer:    string;
    /** How many of them wait to be let in. */
    waiting:   number;
    cards:     Card[];
}

/** What comes back when a card changed: what happened, and the cards as they are now. */
export interface CardChange {
    message:  string;
    card?:    Card;
    cards:    Cards;
}

/** A session or a charge detail record, in the same few words whatever its OCPI version. */
export interface Charged {
    kind:      'session' | 'cdr';
    version:   string;
    id:        string;
    /** Who pushed it: "DE*ABC". */
    party:     string;
    /** A session's: ACTIVE, COMPLETED, ...; null for a charge detail record. */
    status:    string | null;
    start:     string | null;
    end:       string | null;
    kWh:       number | null;
    cost:      number | null;
    currency:  string | null;
    location:  string | null;
    address:   string | null;
    evse:      string | null;
    /** The card's UID or the contract it was charged with. */
    token:     string | null;
}

/** What a driver charged with their cards and contracts, the newest first. */
export interface Charging {
    sessions:   Charged[];
    cdrs:       Charged[];
    /** How many cards of theirs were let in. */
    cards:      number;
    /** How many contracts of theirs are not taken back. */
    contracts:  number;
}


/**
 * Sign up at the HTTPExt API's own sign-up - Hermod's opt-in, which the EMSP
 * attaches when its configuration allows it - and answer with who is now
 * signed in: the sign-up hands out the session itself, and the EMSP puts the
 * account into the driver group before it does. Through extRequest, whose
 * deadline covers the whole answer, and whose refusal is Hermod's sentence.
 */
async function signUp(username:     string,
                      email:        string,
                      password:     string,
                      displayName?: string): Promise<Me> {

    try
    {
        await extRequest('POST', '/auth/signup', { username, email, password, displayName: displayName || undefined });
    }
    catch (problem)
    {
        // Where the configuration leaves the sign-up off there is no route
        // for it, and Hermod answers a path it has no route for with a 404
        // and "Unknown path segment!" - a sentence about Hermod, which is
        // what a driver was shown. The sign-up itself never answers 404.
        if (problem instanceof ApiError && problem.status === 404)
            throw new ApiError(404, 'Signing up is switched off at this EMSP.', null);

        throw problem;
    }

    return request<Me>('GET', '/auth/me');

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

    /** The RFID cards: one's own, or every driver's for the operator, who lets them in. */
    cards: {

        get:      ()                                => request<Cards>('GET', '/cards'),

        /** A card of one's own, entered; it charges once the operator lets it in. */
        add:      (uid: string, label: string)      => request<CardChange>('POST', '/cards', { uid, label: label || undefined }),

        approve:  (uid: string)                     => request<CardChange>('POST', `/cards/${encodeURIComponent(uid)}/approve`, {}),

        reject:   (uid: string, reason: string)     => request<CardChange>('POST', `/cards/${encodeURIComponent(uid)}/reject`, { reason: reason || undefined }),

        block:    (uid: string)                     => request<CardChange>('POST', `/cards/${encodeURIComponent(uid)}/block`, {}),

        unblock:  (uid: string)                     => request<CardChange>('POST', `/cards/${encodeURIComponent(uid)}/unblock`, {}),

        remove:   (uid: string)                     => request<CardChange>('DELETE', `/cards/${encodeURIComponent(uid)}`)

    },

    /** What whoever is signed in charged with their cards and contracts. */
    charging: () => request<Charging>('GET', '/charging'),

    /** A driver's own account: the password, and leaving. */
    me: {

        /** At the HTTPExt API, which keeps the password: the current one is asked for again. */
        changePassword: (id: string, currentPassword: string, newPassword: string) =>
                            extRequest('SET', `/users/${encodeURIComponent(id)}/password`, { currentPassword, newPassword }),

        /** Every contract taken back, every card taken away, and the account deleted - the username typed again. */
        delete:         (username: string) => request<{ message: string }>('POST', '/me/delete', { username })

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
