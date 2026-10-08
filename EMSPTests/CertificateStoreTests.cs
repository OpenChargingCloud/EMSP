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
    /// The certificate store over the wire, as only an EMSP has it: the kinds
    /// it keeps, and what each of them may be told it is for.
    /// </summary>
    /// <remarks>
    /// What every node's store does - a root uploaded for its uses, changed
    /// and taken back to every use, a usage or a kind refused where it is
    /// typed, an identity with and without its key - is asked of this EMSP by
    /// the node's conformance suite; see EMSPConformance. That suite holds
    /// the answer to the store's own word; what that word is for an EMSP is
    /// said here.
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
        /// page shows them, and what the page offers each of them to be for: a
        /// TLS root and a server certificate the name servers and the time
        /// servers, and nothing else anything, since an EMSP names no listener
        /// an identity could be shown on - and a contract refused, which a
        /// vehicle holds and an EMSP signs.
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

            // The upload's shape, with a group - which makes up a kind nobody
            // knows, and must not make one of a vehicle's kinds kept here.
            var (asKinds, asKindsSaid)      = await Send(http, HttpMethod.Post, "/api/v1/certificates", new JObject(
                                                             new JProperty("kinds",    new JArray(new JObject(new JProperty("kind",  "contract"),
                                                                                                              new JProperty("group", "trustAnchor")))),
                                                             new JProperty("pem",      RootPem("Somebody's Contract Again"))
                                                         ));

            var (_, storeAfter)             = await Send(http, HttpMethod.Get, "/api/v1/certificates");

            Assert.Multiple(() => {

                Assert.That(asKinds,                                  Is.EqualTo(HttpStatusCode.BadRequest), asKindsSaid.ToString());
                Assert.That(((JObject) storeAfter["kinds"]!).Properties().Select(kind => kind.Name),
                            Is.EqualTo(new[] { "v2gRoot", "moRoot", "oemRoot", "tlsRoot", "clientRoot", "tlsServer", "tlsIdentity" }),
                            "a contract is no kind of this store, however it is sent");

                Assert.That(((JObject) store["kinds"]!).Properties().Select(kind => kind.Name),
                            Is.EqualTo(new[] { "v2gRoot", "moRoot", "oemRoot", "tlsRoot", "clientRoot", "tlsServer", "tlsIdentity" }));

                Assert.That(store["trustAnchors"]!.Values<String>(),  Is.EqualTo(new[] { "v2gRoot", "moRoot", "oemRoot", "tlsRoot", "clientRoot" }));
                Assert.That(store["credentials"]!.Values<String>(),   Is.EqualTo(new[] { "tlsIdentity" }));
                Assert.That(store["recognised"]!.Values<String>(),    Is.EqualTo(new[] { "tlsServer" }),
                            "a server certificate is recognised, neither believed nor presented");

                Assert.That(store["keysAreUnencrypted"]!.Value<Boolean>(),  Is.False, "an empty store holds no key");

                Assert.That(store["usages"]!.Values<String>(),        Is.EqualTo(new[] { "dns", "nts" }), "what a page may offer a root");

                foreach (var kind in new[] { "tlsRoot", "tlsServer" })
                {
                    Assert.That(store["kinds"]![kind]!["hasUsages"]!.Value<Boolean>(),  Is.True,                          kind);
                    Assert.That(store["kinds"]![kind]!["usages"]!.Values<String>(),     Is.EqualTo(new[] { "dns", "nts" }), kind);
                }

                // Any kind may be marked with a usage made up, so each may be
                // told something; the page offers these nothing of their own.
                foreach (var kind in new[] { "tlsIdentity", "clientRoot", "v2gRoot", "moRoot", "oemRoot" })
                    Assert.That(store["kinds"]![kind]!["usages"]!.Children().Any(),     Is.False, $"{kind} is offered nothing");

                // Refused before the store is asked, as every kind this store
                // does not keep is by the node's API: naming the ones it keeps.
                Assert.That(contract,                                 Is.EqualTo(HttpStatusCode.BadRequest));
                Assert.That(contractSaid["error"]!.Value<String>(),   Is.EqualTo("'kind' has to be one of v2gRoot, moRoot, oemRoot, " +
                                                                                 "tlsRoot, clientRoot, tlsServer, tlsIdentity."),
                            "a contract is a kind only a vehicle keeps");

                Assert.That(unknown,                                  Is.EqualTo(HttpStatusCode.BadRequest));
                Assert.That(unknownSaid["error"]!.Value<String>(),    Is.EqualTo("'kind' has to be one of v2gRoot, moRoot, oemRoot, " +
                                                                                 "tlsRoot, clientRoot, tlsServer, tlsIdentity."),
                            "the kinds this store keeps, and not every kind there is");

            });

        }

        #endregion

    }

}
