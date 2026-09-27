/*
 * Copyright (c) 2014-2026 GraphDefined GmbH <achim.friedland@graphdefined.com>
 * This file is part of EMSP <https://github.com/OpenChargingCloud/EMSP>
 *
 * Licensed under the Affero GPL license, Version 3.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.gnu.org/licenses/agpl.html
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

#region Usings

using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

using Newtonsoft.Json.Linq;

using NUnit.Framework;

#endregion

namespace cloud.charging.open.EMSP.Tests
{

    /// <summary>
    /// The certificate store over the wire: the kinds an EMSP keeps, and what
    /// a TLS root is for - said at the upload, changed afterwards, taken back
    /// to every use, and a usage the EMSP does not know refused where it is
    /// typed.
    /// </summary>
    /// <remarks>
    /// The usages are the vehicle's tests of the same (EV 6d6a0c6), against
    /// the EMSP's store.
    /// </remarks>
    public class CertificateStoreTests : AEMSPTests
    {

        #region (helpers) RootPem(Name) / Send(HTTP, Method, Path, JSON)

        /// <summary>
        /// A self-signed certificate, as the text of a PEM file base64-encoded -
        /// which is what an upload from the browser turns into.
        /// </summary>
        private static String RootPem(String Name)
        {

            using var key  = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request    = new CertificateRequest($"CN={Name}", key, HashAlgorithmName.SHA256);

            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));

            using var root = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(365));

            return Convert.ToBase64String(Encoding.ASCII.GetBytes(root.ExportCertificatePem()));

        }

        private static async Task<(HttpStatusCode Status, JObject JSON)> Send(HttpClient  HTTP,
                                                                             HttpMethod  Method,
                                                                             String      Path,
                                                                             JObject?    JSON = null)
        {

            using var request   = new HttpRequestMessage(Method, Path);

            if (JSON is not null)
                request.Content = new StringContent(JSON.ToString(), Encoding.UTF8, "application/json");

            using var response  = await HTTP.SendAsync(request);
            var text            = await response.Content.ReadAsStringAsync();

            return (response.StatusCode, text.Length > 0 ? JObject.Parse(text) : new JObject());

        }

        #endregion


        #region AnEMSPKeepsWhatItBelievesAndPresentsAndNothingOnlyAVehicleHolds()

        /// <summary>
        /// The seven kinds of EMSP.StoredCertificateKinds, grouped the way the
        /// page shows them - and a contract refused, which a vehicle holds and
        /// an EMSP signs.
        /// </summary>
        [Test]
        public async Task AnEMSPKeepsWhatItBelievesAndPresentsAndNothingOnlyAVehicleHolds()
        {

            using var http                  = await SignedIn();

            var (_, store)                  = await Send(http, HttpMethod.Get, "/api/v1/certificates");

            var (contract, contractSaid)    = await Send(http, HttpMethod.Post, "/api/v1/certificates", new JObject(
                                                             new JProperty("kind",     "contract"),
                                                             new JProperty("content",  RootPem("Somebody's Contract"))
                                                         ));

            var (unknown, unknownSaid)      = await Send(http, HttpMethod.Post, "/api/v1/certificates", new JObject(
                                                             new JProperty("kind",     "evseRoot"),
                                                             new JProperty("content",  RootPem("Some Root"))
                                                         ));

            Assert.Multiple(() => {

                Assert.That(((JObject) store["kinds"]!).Properties().Select(kind => kind.Name),
                            Is.EqualTo(new[] { "v2gRoot", "moRoot", "oemRoot", "tlsRoot", "clientRoot", "tlsServer", "tlsIdentity" }));

                Assert.That(store["trustAnchors"]!.Values<String>(),  Is.EqualTo(new[] { "v2gRoot", "moRoot", "oemRoot", "tlsRoot", "clientRoot" }));
                Assert.That(store["credentials"]!.Values<String>(),   Is.EqualTo(new[] { "tlsIdentity" }));
                Assert.That(store["recognised"]!.Values<String>(),    Is.EqualTo(new[] { "tlsServer" }),
                            "a server certificate is recognised, neither believed nor presented");

                Assert.That(store["keysAreUnencrypted"]!.Value<Boolean>(),  Is.False, "an empty store holds no key");

                Assert.That(contract,                                 Is.EqualTo(HttpStatusCode.BadRequest));
                Assert.That(contractSaid["error"]!.Value<String>(),   Does.StartWith("This EMSP keeps no certificate of that kind"));

                Assert.That(unknown,                                  Is.EqualTo(HttpStatusCode.BadRequest));
                Assert.That(unknownSaid["error"]!.Value<String>(),    Is.EqualTo("'kind' has to be one of v2gRoot, moRoot, oemRoot, " +
                                                                                 "tlsRoot, clientRoot, tlsServer, tlsIdentity."),
                            "the kinds this store keeps, and not every kind there is");

            });

        }

        #endregion

        #region ARootIsUploadedForTheUsesItIsFor()

        [Test]
        public async Task ARootIsUploadedForTheUsesItIsFor()
        {

            using var http        = await SignedIn();

            var (created, entry)  = await Send(http, HttpMethod.Post, "/api/v1/certificates", new JObject(
                                                   new JProperty("kind",     "tlsRoot"),
                                                   new JProperty("content",  RootPem("Our Clocks' Root")),
                                                   new JProperty("usages",   new JArray("nts"))
                                               ));

            var (other, forAll)   = await Send(http, HttpMethod.Post, "/api/v1/certificates", new JObject(
                                                   new JProperty("kind",     "tlsRoot"),
                                                   new JProperty("content",  RootPem("Everybody's Root"))
                                               ));

            var (_, store)        = await Send(http, HttpMethod.Get, "/api/v1/certificates");

            var roots             = store["certificates"]!["tlsRoot"]!.Children<JObject>().
                                        ToDictionary(root => root["id"]!.Value<String>()!);

            Assert.Multiple(() => {

                Assert.That(created,                                                        Is.EqualTo(HttpStatusCode.Created), entry.ToString());
                Assert.That(entry["usages"]!.Values<String>(),                              Is.EqualTo(new[] { "nts" }));
                Assert.That(other,                                                          Is.EqualTo(HttpStatusCode.Created), forAll.ToString());

                Assert.That(store["usages"]!.Values<String>(),                              Is.EqualTo(new[] { "dns", "nts" }), "what a page may offer");
                Assert.That(store["kinds"]!["tlsRoot"]!["hasUsages"]!.Value<Boolean>(),     Is.True);
                Assert.That(store["kinds"]!["clientRoot"]!["hasUsages"]!.Value<Boolean>(),  Is.False);
                Assert.That(roots[entry ["id"]!.Value<String>()!]["usages"]!.Values<String>(),  Is.EqualTo(new[] { "nts" }));
                Assert.That(roots[forAll["id"]!.Value<String>()!]["usages"]!.Type,          Is.EqualTo(JTokenType.Null),
                            "left out at the upload is for every use");
                Assert.That(store["certificates"]!["clientRoot"]!.Children().Any(),         Is.False);

            });

        }

        #endregion

        #region WhatARootIsForIsChangedAndTakenBackToEveryUse()

        [Test]
        public async Task WhatARootIsForIsChangedAndTakenBackToEveryUse()
        {

            using var http        = await SignedIn();

            var (_, entry)        = await Send(http, HttpMethod.Post, "/api/v1/certificates", new JObject(
                                                   new JProperty("kind",     "tlsRoot"),
                                                   new JProperty("content",  RootPem("Our Resolvers' Root")),
                                                   new JProperty("usages",   new JArray("dns"))
                                               ));

            var path              = $"/api/v1/certificates/{entry["id"]}";

            var (both,  forBoth)  = await Send(http, HttpMethod.Patch, path, new JObject(new JProperty("usages", new JArray("nts", "dns"))));
            var (label, relabel)  = await Send(http, HttpMethod.Patch, path, new JObject(new JProperty("label",  "Our Root")));
            var (every, forAll)   = await Send(http, HttpMethod.Patch, path, new JObject(new JProperty("usages", JValue.CreateNull())));
            var (off,   switched) = await Send(http, HttpMethod.Patch, path, new JObject(new JProperty("active", false)));
            var (gone,  left)     = await Send(http, HttpMethod.Delete, path);
            var (after, _)        = await Send(http, HttpMethod.Get, path);

            Assert.Multiple(() => {

                Assert.That(both,                                      Is.EqualTo(HttpStatusCode.OK), forBoth.ToString());
                Assert.That(forBoth["usages"]!.Values<String>(),       Is.EqualTo(new[] { "dns", "nts" }));
                Assert.That(label,                                     Is.EqualTo(HttpStatusCode.OK), relabel.ToString());
                Assert.That(relabel["label"]!.Value<String>(),         Is.EqualTo("Our Root"));
                Assert.That(relabel["usages"]!.Values<String>(),       Is.EqualTo(new[] { "dns", "nts" }), "a PATCH without them leaves them alone");
                Assert.That(every,                                     Is.EqualTo(HttpStatusCode.OK), forAll.ToString());
                Assert.That(forAll["usages"]!.Type,                    Is.EqualTo(JTokenType.Null),    "null is every use again");
                Assert.That(off,                                       Is.EqualTo(HttpStatusCode.OK), switched.ToString());
                Assert.That(switched["active"]!.Value<Boolean>(),      Is.False);

                Assert.That(EMSP.Log.Recent(200, Tag: "security").Any(line => line.Message.Contains("is now for every use")),
                            Is.True,
                            "a change of what a root vouches for is a matter of security, and said as one");

                Assert.That(gone,                                      Is.EqualTo(HttpStatusCode.OK), left.ToString());
                Assert.That(left["certificates"]!["tlsRoot"]!.Children().Any(),  Is.False, "the answer is the store without it");
                Assert.That(after,                                     Is.EqualTo(HttpStatusCode.NotFound));

            });

        }

        #endregion

        #region WhatIsNotAUsageIsRefusedWhereItIsTyped()

        [Test]
        public async Task WhatIsNotAUsageIsRefusedWhereItIsTyped()
        {

            using var http              = await SignedIn();

            var (unknown, said)         = await Send(http, HttpMethod.Post, "/api/v1/certificates", new JObject(
                                                         new JProperty("kind",     "tlsRoot"),
                                                         new JProperty("content",  RootPem("Some Root")),
                                                         new JProperty("usages",   new JArray("ntp"))
                                                     ));

            var (onClients, clientsSaid) = await Send(http, HttpMethod.Post, "/api/v1/certificates", new JObject(
                                                         new JProperty("kind",     "clientRoot"),
                                                         new JProperty("content",  RootPem("Our Partners' Root")),
                                                         new JProperty("usages",   new JArray("nts"))
                                                     ));

            var (notAList, listSaid)    = await Send(http, HttpMethod.Post, "/api/v1/certificates", new JObject(
                                                         new JProperty("kind",     "tlsRoot"),
                                                         new JProperty("content",  RootPem("Another Root")),
                                                         new JProperty("usages",   "dns")
                                                     ));

            var (_, store)              = await Send(http, HttpMethod.Get, "/api/v1/certificates");

            Assert.Multiple(() => {
                Assert.That(unknown,                       Is.EqualTo(HttpStatusCode.BadRequest));
                Assert.That(said.ToString(),               Does.Contain("'ntp' is not a usage this EMSP knows").And.Contain("dns, nts"));
                Assert.That(onClients,                     Is.EqualTo(HttpStatusCode.BadRequest));
                Assert.That(clientsSaid.ToString(),        Does.Contain("only a TLS root and a server certificate"));
                Assert.That(notAList,                      Is.EqualTo(HttpStatusCode.BadRequest));
                Assert.That(listSaid.ToString(),           Does.Contain("has to be a list of usages"));
                Assert.That(store["certificates"]!.Values().SelectMany(kind => kind.Children()).Any(),
                            Is.False,
                            "nothing refused was half-imported");
            });

        }

        #endregion

    }

}
