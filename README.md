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

This is built the same way as
[ChargingStation](https://github.com/OpenChargingCloud/ChargingStation),
[LocalController](https://github.com/OpenChargingCloud/LocalController) and
[CSMS](https://github.com/OpenChargingCloud/CSMS), and everything that is not
OCPI - the DNS and NTS configuration, the accounts, the event log, the clock -
is the same code doing the same thing.


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

| Role | May |
|------|-----|
| `driver` | ask for a contract certificate of their own, and see and revoke the ones they hold - and nothing else |
| `viewer` | read the configuration, the log, and what the partners sent |
| `emsp` | that, and change the name and time servers, test them, issue and take away tokens, and see and revoke every contract |
| `systemadmin` | everything, which adds the roaming partners |

Adding a partner is the highest of these because it hands a foreign system
the right to push into this EMSP and to ask it about its customers; issuing
tokens is the daily work. The driver is the one role nobody hands out:
signing up puts an account there, see below.

```csharp
var emsp = new EMSP(HTTPPort: IPPort.Parse(2355));

emsp.HTTPServer     // the one server everything is registered within
emsp.ExtAPI         // the accounts at /ext
emsp.API            // the JSON API at /api
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
| Configuration | nothing - it answers "what am I running" | `readConfiguration` |
| DNS client | the name servers and how they are asked; a test lookup | `changeNetworkSettings`, `runDiagnostics` |
| NTS client | the time server and how it is asked; a synchronisation | `changeNetworkSettings`, `runDiagnostics` |
| OCPI | nothing - who this EMSP is, and where its endpoints are | `readConfiguration` |
| Roaming partners | who may call this EMSP, and the peering with them | `manageRoamingPartners` |
| Tokens | what this EMSP handed its customers | `manageTokens` |
| Contracts | a contract certificate of one's own; every contract, for the operator | `issueContracts`, `manageContracts` |
| Locations, Tariffs, Charging sessions, Charge detail records | nothing - what the partners pushed | `readConfiguration` |
| Logs | nothing - it reads | `readConfiguration` |


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
under `Frontend/src` changed), then embeds every file of `Frontend/dist` as a
manifest resource named `cloud.charging.open.EMSP.HTTPRoot.<path>` - which is
what Hermod's `EmbeddedContentSource` reads and `MapSinglePageApplication`
serves.

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

Each test gets an EMSP of its own, on a port the operating system has just
confirmed is free and with its own directory for the files an EMSP writes.
**They never touch the network**: the time client is switched off before each
EMSP is built, the DNS client is only ever asked what it is configured as, and
the stub CPO listens on the loopback address.


## The clock and the log

The same as in the CSMS, and for the same reasons: `EMSP` takes a
`TimeProvider` as its last constructor parameter and hands it to everything
that asks what time it is; the clock is checked against the time server every
fifteen minutes and never set from the answer; and every entry of the log
carries a timestamp, a level and tags - `ocpi`, `partner`, `credentials`,
`tokens`, `locations`, `sessions`, `cdrs`, `dns`, `nts`, `web`, `auth`, ... -
that the Logs page filters on.


## Your participation

This software is Open Source under the **Affero GPL 3.0 license**.
We appreciate your participation in this ongoing project, and your help to
improve it and the e-mobility ICT in general. If you find bugs, want to
request a feature or send us a pull request, feel free to use the normal
GitHub features to do so. For this please read the Contributor License
Agreement carefully and send us a signed copy or use a similar free and
open license.
