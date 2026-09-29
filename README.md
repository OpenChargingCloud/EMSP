# EMSP

One e-mobility service provider speaking OCPI, with a web interface in front
of it: a C# HTTP backend built on [Hermod](https://github.com/Vanaheimr/Hermod)
and the [WWCP OCPI](https://github.com/OpenChargingCloud/WWCP_OCPI) library,
and a frontend of HTML, SCSS and TypeScript bundled by webpack and embedded
into the assembly - so the EMSP is one binary to deploy and needs nothing
installed beside it.

Nothing is rendered on the server. The browser loads one bundle and talks to
the EMSP over a JSON API and one Server-Sent Events stream.

```
  browser  ──  GET  /                            the SPA stub and the bundle
           ──  POST /ext/login                   the session cookie
           ──  GET  /api/v1/configuration        what the EMSP is made of
           ──  GET  /api/v1/ocpi/partners        the roaming partners
           ──  GET  /api/v1/ocpi/tokens          the customers' tokens
           ──  GET  /api/v1/ocpi/locations       what the partners pushed
           ──  GET  /api/v1/logs                 what happened up to now
           ──  GET  /api/v1/events               and everything from now on (SSE)

  a CPO    ──  GET  /ext/versions                where everything else is
           ──  POST /ext/v2.2.1/credentials      the peering
           ──  PUT  /ext/v2.2.1/emsp/locations/… what it operates
           ──  GET  /ext/v2.2.1/emsp/tokens      whom this EMSP vouches for
```

An EMSP is the other end of every roaming agreement. The charge point
operators it is peered with push their locations, tariffs, sessions and
charge detail records into it, ask it whether a customer's token may charge,
and take commands from it. That is what the web interface is for: it is the
one place where somebody can see which partners are registered, what they
sent, and which of the customers' tokens are out there - without reading a
log file over somebody else's shoulder.

Below it is [WWCP_Node](https://github.com/OpenChargingCloud/WWCP_Node): what
every one of these programs is before it is anything in particular - the log,
the configuration file, name resolution and the time, a certificate store, the
accounts, and the HTTP server with the web interface behind it. The vehicle of
[EV](https://github.com/OpenChargingCloud/EV) is one of those with a battery,
the [charging station](https://github.com/OpenChargingCloud/ChargingStation)
one with EVSEs; this EMSP is one with the OCPI endpoints its roaming partners
call, the tokens it hands its customers, and the contracts of its drivers.

What is the EMSP's own on top of the node: its sections of the same
configuration file - `ocpi` and `contracts` - read from the document the node
has already read; the roles its accounts know, which the node makes a group of
at every start; the routes it adds to the node's JSON API below `/api` - the
roaming partners, the tokens and what the partners pushed, and the contracts;
what it says once it is up and what it ends before the server stops; and its
own cards on the Configuration page. What the node does on its own - the
file's sections, the log, the time servers, the certificate store, the
accounts, the port, and the JSON API every node answers, from the status and
the clock to the log and its event stream - is tested once more in
WWCP_Node's own `WWCP_Node_Tests`, against a node of no particular kind.


## Who may open it: `HTTPExtAPI`

The HTTP server of this EMSP carries Hermod's `HTTPExtAPI` - accounts,
groups, organizations and API keys, kept in a directory of its own - at
`/ext`, beside the JSON API at `/api` and the web interface at `/`.

The roles are groups in it, and the names overlap with the other components
on purpose: all of them have `systemadmin` and `viewer`. What does not overlap
is the operator role - a CPO's operator is not an EMSP's - so `emsp` here is
what `cpo` is there. One `HTTPExtAPI` handed to several of these programs
makes an account in `systemadmin` an administrator of every one of them at
once, with each still deciding for itself what a role permits, which is what
[EVChargingTestEnvironment](https://github.com/OpenChargingCloud/EVChargingTestEnvironment)
does with `--shared`.

What a role may do is an operation - `read`, `edit` or `run` - on a
resource: the node's `configuration`, `dns`, `nts` and `certificates`, and the
EMSP's `ocpi` (who it is in OCPI, and what the partners pushed), `partners`,
`tokens` and `contracts`. Four roles, when the configuration file says nothing
else:

| Role | May |
|------|-----|
| `driver` | `contracts:run` - ask for a contract certificate of their own, and see and revoke the ones they hold - and nothing else |
| `viewer` | read everything: the configuration, the log, the certificates, and what the partners sent |
| `emsp` | that, and change the name and time servers and test them (`dns`, `nts`: edit, run), issue and take away tokens (`tokens:edit`), and see and revoke every contract (`contracts:edit`) |
| `systemadmin` | everything, which adds the roaming partners and the certificates |

`viewer` and `systemadmin` are the node's, the other two are in
`EMSPAccess.cs`. Adding a partner is the highest of these because it hands a
foreign system the right to push into this EMSP and to ask it about its
customers; adding a root is next to it, because it makes this EMSP believe a
server nobody else would; issuing tokens is the daily work. The driver is the
one role nobody hands out: signing up puts an account there, see below.
Seeing everybody's contracts is `contracts:edit` rather than a read, because
which contracts exist says who the customers are, and the viewer reads
everything else.

The configuration file may add roles and say differently what one of them may
do - `"roles": { "support": [ "dns:read", "nts:read" ] }` - see
[WWCP_Node's README](https://github.com/OpenChargingCloud/WWCP_Node#who-may-sign-in).
A role the file widens is widened for everybody in it, the drivers who signed
themselves up included.

The event stream the Logs page follows is one request answered for hours, so
it is asked again, before every entry it is sent and at every heartbeat,
whether whoever opened it would still be let in: the session still there, the
password still the account's, the API key neither taken back nor run out, the
account still one that may sign in and still one that may read the log. A
sign-out in another tab, a new password, a key taken back or an account taken
out of the `emsp` group ends it, and the Logs page does not go on saying
"reconnecting ...": it goes to the sign-in, or says that this account may no
longer read the log.

```csharp
var emsp = new EMSP(HTTPPort: IPPort.Parse(2355));

emsp.HTTPServer     // the one server everything is registered within
emsp.ExtAPI         // the accounts at /ext
emsp.API            // the node's JSON API at /api, with the EMSP's routes
emsp.OCPIAPI        // the library's Common HTTP API: the versions list
emsp.OCPIVersions   // one binding per OCPI version offered
```


## OCPI

The library keeps a Common API per OCPI version - 2.1.1 and 2.2.1 are offered
unless `ocpi.versions` in the configuration says otherwise, and 2.3.0 is
offered on request only, see below - and each
of those has its own roaming partners, its own tokens and its own store of
what the partners sent, because the data structures differ between the
versions. The web interface wants one list of each, so every version answers
the same questions behind `OCPIVersion` and the EMSP puts the answers side by
side. A partner is on exactly one version: the one it was added under, which
is the one it registers on.

**Where the endpoints are.** The library attaches to the `HTTPExtAPI` rather
than to the server, and it builds the URLs it advertises - in the versions
list and in the version details - from the Host header and its own prefix, as
if that API sat at the root of the server. Here it sits at `/ext`. So the OCPI
endpoints are registered directly below the `HTTPExtAPI`, and its root path is
handed to the library as the prefix it puts into the URLs it advertises. Both
then agree, which is what a partner that follows the versions list needs; the
tests say so for every endpoint the version details name.

```
  http://127.0.0.1:2355/ext/versions                    ← the one URL a partner is given
  http://127.0.0.1:2355/ext/versions/2.2.1
  http://127.0.0.1:2355/ext/v2.2.1/credentials
  http://127.0.0.1:2355/ext/v2.2.1/emsp/locations
  http://127.0.0.1:2355/ext/v2.2.1/emsp/tokens
```

Behind a reverse proxy or a public name, `ocpi.externalURL` says what a
partner can reach, and the advertised URLs are built from that.

**The peering, both ways round.** Adding a partner on the Roaming partners
page hands the operator a token, which they give to the partner; the partner
fetches the versions with it and POSTs its credentials, and the library fills
in the rest. Added with the partner's own token and versions URL as well, the
EMSP can go to them instead: the *Register* button fetches their versions,
finds their credentials endpoint and POSTs this EMSP's credentials with a
fresh token for them - and every step of it is in the log. Removing a partner
shuts its token out the moment it is gone; what it pushed stays, because that
is a record and not a setting.

**What is written down.** The library keeps its partners and its assets in
append-only files of its own, below an `ocpi` directory beside the
configuration file - one set per version - and reads them back at every
start. Nothing about OCPI is therefore in `configuration.json` but who this
EMSP is and which versions it offers:

```json
{
  "ocpi": {
    "countryCode":  "DE",
    "partyId":      "GDF",
    "name":         "GraphDefined EMSP",
    "website":      "https://open.charging.cloud",
    "versions":     [ "2.1.1", "2.2.1", "2.3.0" ],
    "externalURL":  "https://emsp.example.org",
    "locationsAsOpenData":  false,
    "tariffsAsOpenData":    false,
    "allowDowngrades":      false,
    "logging":      { "requests": true, "payloads": false }
  }
}
```

Read once, at the start, and deliberately *not* changeable while running: a
country code and a party identification are what every partner knows this
EMSP by and what it wrote into their credentials, and changing them under a
live registration would not rename the EMSP, it would make it a second one
nobody is registered with.

**2.3.0, on request only.** The library's Common API for 2.3.0 files a pushed
location under the party that owns it and knows only this EMSP's own parties,
so a CPO's push is turned away as coming from an unknown party; making the CPO
one of this EMSP's parties would let the push through and, in the same breath,
make the version details advertise CPO modules this EMSP does not serve. Until
the library keeps remote CPOs apart for this version as it does for 2.2.1,
2.3.0 is a version a partner can register on and fetch tokens from, and
nothing more - which is why `"versions": [ "2.1.1", "2.2.1", "2.3.0" ]` has to
be written down to get it. The library also advertises a charging profiles
module in the version details of an EMSP and serves no route for it; the OCPI
page lists what answers.


## What it can be told

| Page | What it changes | Permission |
|------|-----------------|------------|
| Configuration | nothing - it answers "what am I running" | `configuration:read` |
| DNS client | the name servers, how they are asked and what their certificates are held to; a lookup, of all of them or of one | `dns:edit`, `dns:run` |
| NTS client | the time servers, what their certificates are held to, and the rules for believing them; a synchronisation, and a test of one server | `nts:edit`, `nts:run` |
| Certificates | the roots this EMSP believes, the certificate it presents, the servers it recognises | `certificates:edit` |
| OCPI | nothing - who this EMSP is, and where its endpoints are | `ocpi:read` |
| Roaming partners | who may call this EMSP, and the peering with them | `partners:edit`, `partners:run` |
| Tokens | what this EMSP handed its customers | `tokens:edit` |
| Contracts | a contract certificate of one's own; every contract, for the operator | `contracts:run`, `contracts:edit` |
| Locations, Tariffs, Charging sessions, Charge detail records | nothing - what the partners pushed | `ocpi:read` |
| Logs | nothing - it reads | `configuration:read` |


## Name servers and time servers

Both are the node's rather than the EMSP's: the `dns` and `nts` sections of
the configuration file (`--config <file>`), read and written the way every one
of these programs reads and writes them, so that a file written for a
charging station, a vehicle or a CSMS says the same to an EMSP, and one file
can be copied between them:

```json
{
  "dns": { "enabled": true, "servers": [ { "address": "9.9.9.9" } ], "useCache": true },
  "nts": { "enabled": true, "servers": [ "ptbtime1.ptb.de", "ptbtime2.ptb.de",
                                         "ptbtime3.ptb.de", "ptbtime4.ptb.de" ],
           "minServers": 2 }
}
```

What their keys are, what each of them is when the file says nothing, and how
a group of time servers is asked, agreed on and held to its certificates is
written down once, in
[WWCP_Node's README](https://github.com/OpenChargingCloud/WWCP_Node#name-resolution-and-the-time).
What a section does not mention is left as it is, and a section that is
missing leaves everything as the EMSP was built. So is a third section,
`certificates`, which says where the node keeps its certificate store:
`certificates/` beside the configuration file unless it says otherwise - see
[below](#certificates-and-where-they-live).

What the EMSP adds is the way in. Its DNS client and NTS client pages read and
change the two sections through `/api/v1/configuration/dns` and
`/api/v1/configuration/nts`, and ask from there. The DNS page asks all the
name servers at once, the way the EMSP resolves anything else, or one of them
alone from its own row. The NTS page asks the whole group with "Sync now", the
way the clock check does, or one server with the Test of its row - the name,
the TLS handshake and what the server's certificate claims, down to the root
CA it ends at and that root's SHA-256 fingerprint, the key exchange and the
authenticated request, each step timed. Neither steps the clock.

A time server, and a name server asked over TLS or HTTPS, can be held to a
certificate or a root, and the pages are where that is said: a server's dialog
takes SHA-256 fingerprints one to a line, adds the one the server showed last
or one the certificate store keeps for it with a click, and says what a
mismatch comes to and whether the server is held to what it is first believed
with. Its row says what was made of its certificate the last time - believed,
used although it did not match, or refused, and why - what it is held to, and
when it showed another certificate than before. A lookup on the DNS page says
the same of every certificate it met. What a server was last believed with is
kept in `known-servers.json` beside the configuration file, pinned or not, so
that another certificate is noticed after a restart as well.

The whole list goes to the EMSP at every save, so every server goes with what
it is held to, and the pages' `ntsServers.ts`, `dnsServers.ts` and `pins.ts`
are where that is decided and tested: a list sent without the pins of the
servers nobody touched would let go of them, the ones learned on first use
included. What is learned is written into a server's entry at the first key
exchange or handshake after a save, mostly with the page still open, so every
server the page loaded also goes with what the page showed it held to, under
`pinsAsShown`: the next save keeps what was learned in between, and still
takes away a pin that was shown and removed there. A name server switched to a
transport that shows no certificate lets go of its pins when it is saved - the
EMSP would refuse them - and its row says so first. Holding a server to a fingerprint is the operator's, with the rest
of the server (`dns:edit`, `nts:edit`): a pin cannot make the EMSP believe a
certificate that chains to nothing this machine or its store holds, and what
goes into the store stays the administrators'.

A page holding what has not been saved - a list of name servers edited on
screen, the rules of the group typed and not sent - asks before it is left,
whether by its own Reload, by the menu or by the browser. And every request a
page makes has a deadline: a lookup or a test is given as long as the name
servers or the time servers may take, and fifteen seconds on top, and anything
else fifteen seconds to read and thirty to write, after which the page says
that the EMSP did not answer instead of waiting as long as the browser will.

The check runs by itself every `nts.checkEverySeconds`, the first one a minute
after starting. A new interval, and switching NTS off or on, reach a running
check at once. What the clock is worth - the time, against which group it was
checked and how many of it had to answer, how long ago and how far off, and
whether all of that adds up to legal time and why not - is served at
`GET /api/v1/clock`, where every node has it, and is the first card of the NTS
page.


## Certificates, and where they live

Everything this EMSP believes, everything it presents and every server it
recognises is in one store - WWCP_Node's `CertificateStore`, a directory of
files with an `index.json` beside them - and is addressed by a short handle
rather than by a path. The Certificates page shows it and changes it, through
`/api/v1/certificates`.

Seven kinds, in three groups. A **root** is what this EMSP believes, any
number of each switched on at once: `tlsRoot` for a server it connects to - a
time server, or a name server over TLS or HTTPS - beside the roots of the
machine it runs on; `clientRoot` for a client connecting to it; and the three
roots of Plug & Charge, `v2gRoot`, `moRoot` and `oemRoot`, kept apart because
one bag of roots would let an OEM root vouch for a contract. A **TLS
identity** - `tlsIdentity` - is what it presents, with its private key. A
**server certificate** - `tlsServer` - is neither: what a server shows, kept
so that the server can be held to it by its fingerprint, and never with a
private key. The TLS roots and the server certificates are what the EMSP uses
today; the others are kept, and nothing here checks a chain against them or
presents one yet. What only a vehicle holds - its own certificate, its
contracts, its provisioning certificate, the one it checks a tariff with - is
refused. The seven are `EMSP.StoredCertificateKinds`.

A TLS root and a server certificate are told what they are for: the time
servers (`nts`), the name servers (`dns`), or - with nothing said - every use.
The Certificates page asks at the upload and again with **Uses**, because one
root may vouch for both, and a root kept for the name servers alone vouches for
no time. A TLS identity is told the listeners it is shown on where a kind of
node names some; an EMSP names none, so the page offers an identity nothing to
be told.

The store holds private keys **unencrypted**: a PKCS#12 is opened with its
password once, at import, and written back without one. The file system is what
guards them, and the EMSP says so at every start and at every import.

Reading the store is `certificates:read`, which every role but the driver has.
Changing it is `certificates:edit`, which only the administrators have unless
the configuration file says otherwise.

The contract PKI is not in it and does not go into it. The MO root the
contracts are signed below and the two sub-CAs under it carry their private
keys, which a store of roots to believe must never hold: they stay in `pki/`,
see below.


## Contract certificates: the mobility operator's side of Plug & Charge

A contract certificate is what a vehicle presents at a charging station
instead of a card: an eMAID, a public key, and the signature of a mobility
operator the charge point operator trusts. This EMSP is that mobility
operator. At its first start it makes an MO root with two sub-CAs below it,
built to the profiles of the ISO 15118 PKI builder - so that a station and
its CSMS see from this EMSP what they see from the reference hierarchies of
the test environment - and keeps them below `pki/mo/` beside the
configuration, private keys and all. A root made afresh would invalidate
every contract ever issued, so a directory that is there but incomplete is an
error at the start and never a reason to make a new one.

```
  driver's browser                          EMSP
  ────────────────                          ────
  POST /ext/auth/signup                 →   an account, in the driver group
  WebCrypto: a P-256 key pair, kept here
  a PKCS#10 request, signed with it     →   POST /api/v1/contracts { csr }
                                        ←   the certificate to a fresh eMAID,
                                            the two sub-CAs, the MO root
  a PKCS#12 of key + certificate + sub-CAs,
  encrypted with a password of the driver's   → the vehicle's certificate store
  mo-root.pem                                  → the vehicle's and the CPO's trust
```

**The key never leaves the browser.** The EMSP checks that the request is
signed with the key it carries - the proof that whoever sent it holds the
private half - refuses anything but secp256r1, which is the one curve a
vehicle signs its authorization with, and takes nothing else from the
request: the subject is its own to decide. The common name is the eMAID
without separators, `DEGDFC12345678X`, which is what a vehicle reads out of
it; people and OCPI read it with hyphens, `DE-GDF-C12345678-X`. The check
digit is ISO 15118-1 Annex H, and `EMAId` calculates and checks it.

**Every contract is a token.** The eMAID goes into the tokens of every OCPI
version this EMSP speaks, of type `OTHER`, so that a roaming partner that
asks about it gets the answer the certificate already gave. Revoking a
contract takes the tokens with it; the certificate stays on disk, as a
record, below `pki/contracts/` beside `index.json`, which says whom each one
belongs to. There is no CRL and no OCSP: a CPO that wants to know asks the
EMSP over OCPI, which is what it does for every other token.

**Signing up** is Hermod's own opt-in `SelfSignUpAPI` - `POST
/ext/auth/signup` with a username, an e-mail address and a password - which
makes an account and signs it in. What this EMSP adds, through the API's
`OnSignedUp`, is where the account lands: in the EMSP's organization, so
that the sign-in door opens for it tomorrow, and in the `driver` group, so
that it may ask for contracts and look at nothing else - not the
configuration, not the log, not the partners.

```json
{
  "contracts": {
    "selfSignUp":    true,
    "validityDays":  730
  }
}
```

Read once, at the start. Without the section anybody may sign up and a
contract is good for two years, which is what the ISO 15118-2 profile gives
one.

**On the bench.** The vehicle imports the PKCS#12 as its contract and
`mo-root.pem` as an MO root - on its Certificates page, or with
`--import-certificate moRoot=mo-root.pem --import-certificate
contract=contract-DEGDF….p12 --certificate-password …` on the command line
of [EVCLI](https://github.com/OpenChargingCloud/EVCLI). The charging station
and its CSMS have to hold the same MO root to accept the contract;
`GET /api/v1/contracts/mo-root.pem` and the file the console names at every
start are where to get it.


## Running it

```
dotnet run --project EMSPCLI
```

in [EMSPCLI](https://github.com/OpenChargingCloud/EMSPCLI), which is the
command line that builds one and the submodules it is built from.

At the first start there are no accounts, so the EMSP makes one up - `root`,
under `accounts/` beside the configuration - and prints its password once.
Then open http://127.0.0.1:2355/ and sign in. Signing in happens at Hermod's
HTTPExt API, mounted under `/ext` - the same door the other components use.
A driver signs up at http://127.0.0.1:2355/signup instead.

Port 2355, beyond the ports the other OpenChargingCloud boxes use - a vehicle
2347, a charging station 2348 and 2349, a local controller 2350, a CSMS 2351
and its OCPP ports up to 2354 - because all of them are routinely tried out on
the same bench.


## Building

`dotnet build` builds the frontend too: `EMSP.csproj` runs `npm ci` (only when
`Frontend/node_modules` is missing) and `npm run build` (only when something
under `Frontend/src` changed, or under WWCP_Node's), then embeds every file of
`Frontend/dist` as a manifest resource named
`cloud.charging.open.EMSP.HTTPRoot.<path>` - which is what Hermod's
`EmbeddedContentSource` reads and `MapSinglePageApplication` serves.

What every kind of node shows alike is WWCP_Node's, in its `Frontend/src`:
the frame with its menu, the start, the sign-in, the Logs page and what an
address without a page says; the DNS and the NTS page, with what a server's
certificate is held to, and the certificate store; the tagged template the
pages are written in, the router, the base path, what the stub's `<meta>` tags
say, the question before a page's changes are left behind; the client of the
JSON API every node has - its requests, their deadlines and what a page is told
when no answer comes - the log a page follows, who is signed in, the helpers
the pages format and save with; and the stylesheet. It is imported as
`@node/...`: webpack's alias and the tsconfig's `paths` find it beside the EMSP
in `libs/`, and it is bundled into the EMSP's own bundle - the files of the
WWCP_Node this EMSP pins, as with the C#. What stays in the EMSP's `Frontend`
is its own: `main.ts`, one `startNode()` with the EMSP's menu, its pages and
the words of its sign-in and of its certificate store; the pages themselves; in
`api/client.ts` its resources, its status and configuration, the kinds its
store keeps, the sign-up, and its OCPI and contract routes, on top of the
node's; and in `styles/app.scss`, below the node's stylesheet, what only the
EMSP's own pages need.

```
dotnet build                            the whole thing
dotnet build -p:SkipFrontendBuild=true  backend only, reusing the existing dist/
npm run watch     (in Frontend/)        rebuild the bundle as it is edited
npm run typecheck (in Frontend/)        tsc --noEmit
```


## The tests

```
dotnet test libs/EMSP/EMSPTests
```

They start real EMSPs and talk to them over HTTP the way a browser does - and
the way a CPO does: the versions list is fetched, every endpoint the version
details advertise is probed, a partner is added and signs in with the token
it was given, pushes a location and sees it turned away once it was removed;
a token is issued and fetched by a CPO; and the EMSP registers with a stub
CPO of three routes, which records what it was told.

What every node answers alike - the sign-in, the status and the clock, the
configuration, name resolution and the time servers, the log and its event
stream, stopping with browsers watching, the certificate store and the web
interface - is not tested here but in WWCP_Node's conformance suite,
`WWCP_Node_TestKit`, which `EMSPConformance` runs against an EMSP - the same
tests for every kind of node - and what a node does below its API, in
`WWCP_Node_Tests`. What stays here is what only an EMSP says: its OCPI and its
contracts, its sections, its roles and what they may do, the kinds its store
keeps and what each is for, and that its log and its clock are the
operator's.

Each test gets an EMSP of its own, on a port the operating system has just
confirmed is free and with its own directory for the files an EMSP writes.
**They never touch the network**: the time client is switched off before each
EMSP is built, the DNS client is only ever asked what it is configured as, and
the stub CPO listens on the loopback address. The tests of what the `dns` and
`nts` sections do build EMSPs that are never started, or switch name
resolution off, so that nothing is asked of anybody; and what a time server's
certificate is said to be is tested on certificates made on the spot.

The web interface has no test of its own yet: `src/scaffolding.test.ts` holds
the place, and asks what the first will need - that `@node/...` is found from
there, by the type check and by Node. What a certificate of each kind may be
told it is for, what the NTS and the DNS page tell the EMSP when a server is
added, edited, deleted or held to a certificate, what a page asks before work
that was not saved is left behind, what it says when the EMSP does not answer,
and the log it follows are tested with the rest of what every node shows alike,
in WWCP_Node's `Frontend`.

```
npm test            (in Frontend/)   node --test over src/**/*.test.ts
npm run typecheck:test               the tests' own type check
```

`npm test` loads WWCP_Node's `Frontend/test/resolve.ts` first, which tells Node
where `@node/...` is, as the alias tells webpack.


## The clock and the log

Both are the WWCP node's, and so the same as in the CSMS and for the same
reasons: `EMSP` takes a `TimeProvider` as its last constructor parameter and
hands it to the node, which hands it to everything that asks what time it is;
the clock is checked against its group of time servers every fifteen minutes
and never set from the answer; and every entry of the log carries a timestamp,
a level and tags - `ocpi`, `partner`, `credentials`, `tokens`, `locations`,
`sessions`, `cdrs`, `dns`, `nts`, `web`, `auth`, ... - that the Logs page
filters on. The entries about the EMSP itself are tagged `emsp`, and an EMSP
given a directory for its log files - EMSPCLI gives it `logs/` - writes one
per UTC day there, `emsp-2026-09-25.log`, with every entry down to the debug
ones.

A program that reads commands on the same console - EMSPCLI does - hands the
log a way to write around the line being typed, so that an entry arriving
mid-word neither lands inside the command nor waits for it:

```csharp
emsp.ShareConsoleWith(cli.WriteBlock);   // line off, entry whole, line back
```


## Your participation

This software is Open Source under the **Affero GPL 3.0 license**.
We appreciate your participation in this ongoing project, and your help to
improve it and the e-mobility ICT in general. If you find bugs, want to
request a feature or send us a pull request, feel free to use the normal
GitHub features to do so. For this please read the Contributor License
Agreement carefully and send us a signed copy or use a similar free and
open license.
