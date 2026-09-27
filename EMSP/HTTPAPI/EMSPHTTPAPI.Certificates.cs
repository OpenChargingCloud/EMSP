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

using System.Diagnostics.CodeAnalysis;

using Newtonsoft.Json.Linq;

using org.GraphDefined.Vanaheimr.Hermod.HTTP;

using cloud.charging.open.protocols.WWCP.Node.Certificates;
using cloud.charging.open.protocols.WWCP.Node.Web;

#endregion

namespace cloud.charging.open.EMSP
{

    /// <summary>
    /// The part of the JSON API that is about the certificate store: the
    /// roots this EMSP believes, what it presents, and the server
    /// certificates it recognises a server by.
    /// </summary>
    /// <remarks>
    /// Reading at the reading permission, changing at "certificates:edit",
    /// which only the system administrators hold unless the configuration
    /// file says otherwise: somebody who can add a root can make this EMSP
    /// believe a server nobody else would.
    ///
    /// Not the contract PKI. The MO root the contracts are signed below and
    /// the two sub-CAs under it carry their private keys, stay in their own
    /// directory, and have their own route - see EMSPHTTPAPI.Contracts.cs.
    /// </remarks>
    public partial class EMSPHTTPAPI
    {

        #region (private) RegisterCertificateRoutes()

        /// <summary>
        /// Everything under /v1/certificates.
        /// </summary>
        private void RegisterCertificateRoutes()
        {

            AddHandler(HTTPPath.Root + "v1/certificates",          GetCertificates,        HTTPMethod.GET);
            AddHandler(HTTPPath.Root + "v1/certificates",          PostCertificate,        HTTPMethod.POST);
            AddHandler(HTTPPath.Root + "v1/certificates/reload",   PostCertificateReload,  HTTPMethod.POST);
            AddHandler(HTTPPath.Root + "v1/certificates/{id}",     GetCertificate,         HTTPMethod.GET);
            AddHandler(HTTPPath.Root + "v1/certificates/{id}",     PatchCertificate,       HTTPMethod.PATCH);
            AddHandler(HTTPPath.Root + "v1/certificates/{id}",     DeleteCertificate,      HTTPMethod.DELETE);

        }

        #endregion


        #region (private) GetCertificates(Request) / PostCertificate(Request)

        /// <summary>
        /// GET /api/v1/certificates: everything in this EMSP's store.
        /// </summary>
        /// <remarks>
        /// Grouped by kind rather than returned as one list, because the page
        /// that reads it shows the roots this EMSP believes, what it presents
        /// and the servers it recognises as three different things.
        ///
        /// At the reading permission: what certificates an EMSP holds is not a
        /// secret from anybody who may look at it at all, and the private keys
        /// are not in the answer.
        /// </remarks>
        private Task<HTTPResponse> GetCertificates(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Read(NodeResources.Certificates), false, out _, out var refused))
                return Task.FromResult(refused);

            return Task.FromResult(
                       JSONResponse(Request, HTTPStatusCode.OK, EMSP.CertificatesJSON())
                   );

        }

        /// <summary>
        /// POST /api/v1/certificates with {"kind", "content", "password", "label",
        /// "usages"}: put a certificate into the store.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The file arrives as base64 in <c>content</c>, which is what an
        /// upload from the browser turns into. The password is what opens it if
        /// it is a protected PKCS#12, is used once here, and is not kept: the
        /// store writes what it holds without one.
        /// </para>
        /// <para>
        /// Answered with 200 rather than 201 when the certificate was already
        /// there. Importing the same file twice is the same entry - the id is
        /// its fingerprint - so the second import created nothing.
        /// </para>
        /// <para>
        /// <c>usages</c> says what a TLS root or a server certificate is for -
        /// ["dns", "nts"] - and is left out, or null, for every use.
        /// </para>
        /// </remarks>
        private Task<HTTPResponse> PostCertificate(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Edit(NodeResources.Certificates), true, out _, out var refused))
                return Task.FromResult(refused);

            if (!TryParseJSONObject(Request, out var json, out var errorResponse))
                return Task.FromResult(errorResponse);

            // The kinds this store keeps, not every kind there is: naming one
            // it refuses would be naming a refusal.
            if (!CertificateKindExtensions.TryParseKind(json.Value<String>("kind"), out var kind))
                return Task.FromResult(
                           ErrorJSON(Request, HTTPStatusCode.BadRequest,
                                     "'kind' has to be one of " +
                                     String.Join(", ", EMSP.Certificates.Kinds.Select(one => one.AsText())) + ".")
                       );

            var content = json.Value<String>("content")?.Trim();

            if (content is null or { Length: 0 })
                return Task.FromResult(
                           ErrorJSON(Request, HTTPStatusCode.BadRequest,
                                     "'content' has to be the certificate file, base64-encoded.")
                       );

            Byte[] bytes;

            try
            {
                bytes = Convert.FromBase64String(content);
            }
            catch (FormatException)
            {
                return Task.FromResult(
                           ErrorJSON(Request, HTTPStatusCode.BadRequest, "'content' is not valid base64.")
                       );
            }

            if (!TryReadUsages(json, out var usages, out var usagesError))
                return Task.FromResult(ErrorJSON(Request, HTTPStatusCode.BadRequest, usagesError));

            var existed = EMSP.Certificates.Entries.Count;

            if (!EMSP.Certificates.Import(bytes,
                                          kind,
                                          json.Value<String>("password"),
                                          json.Value<String>("label"),
                                          usages,
                                          out var entry,
                                          out var error))
            {
                return Task.FromResult(ErrorJSON(Request, HTTPStatusCode.BadRequest, error));
            }

            return Task.FromResult(
                       JSONResponse(
                           Request,
                           EMSP.Certificates.Entries.Count > existed
                               ? HTTPStatusCode.Created
                               : HTTPStatusCode.OK,
                           entry.ToJSON(WithDiagnostics: true)
                       )
                   );

        }

        #endregion

        #region (private) GetCertificate(Request) / PatchCertificate(Request) / DeleteCertificate(Request)

        /// <summary>
        /// GET /api/v1/certificates/{id}: one certificate.
        /// </summary>
        private Task<HTTPResponse> GetCertificate(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Read(NodeResources.Certificates), false, out _, out var refused))
                return Task.FromResult(refused);

            var entry = EMSP.Certificates.Get(HandleOf(Request));

            return Task.FromResult(
                       entry is null
                           ? ErrorJSON(Request, HTTPStatusCode.NotFound, "There is no such certificate in this store.")
                           : JSONResponse(Request, HTTPStatusCode.OK, entry.ToJSON(WithDiagnostics: true))
                   );

        }

        /// <summary>
        /// PATCH /api/v1/certificates/{id} with {"active"}, {"label"} and/or
        /// {"usages"}: switch a certificate on or off, rename it, or say what
        /// it is for.
        /// </summary>
        /// <remarks>
        /// Three things in one request because they are the only three things
        /// about a stored certificate that can be changed at all - everything
        /// else about it is read out of the file and is not somebody's to edit.
        /// "usages" set to null is every use again; left out, it is left alone.
        /// </remarks>
        private Task<HTTPResponse> PatchCertificate(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Edit(NodeResources.Certificates), true, out _, out var refused))
                return Task.FromResult(refused);

            if (!TryParseJSONObject(Request, out var json, out var errorResponse))
                return Task.FromResult(errorResponse);

            var handle = HandleOf(Request);

            if (EMSP.Certificates.Get(handle) is null)
                return Task.FromResult(
                           ErrorJSON(Request, HTTPStatusCode.NotFound, "There is no such certificate in this store.")
                       );

            if (json.TryGetValue("label", out var label) && label.Type != JTokenType.Undefined)
            {
                if (!EMSP.Certificates.Relabel(handle, label.Value<String>(), out _, out var relabelError))
                    return Task.FromResult(ErrorJSON(Request, HTTPStatusCode.BadRequest, relabelError));
            }

            if (json.TryGetValue("active", out var active))
            {

                if (active.Type != JTokenType.Boolean)
                    return Task.FromResult(
                               ErrorJSON(Request, HTTPStatusCode.BadRequest, "'active' has to be true or false.")
                           );

                if (!EMSP.Certificates.SetActive(handle, active.Value<Boolean>(), out _, out var activeError))
                    return Task.FromResult(ErrorJSON(Request, HTTPStatusCode.BadRequest, activeError));

            }

            // Present, even as null, is something to set: null is every use.
            if (json.ContainsKey("usages"))
            {

                if (!TryReadUsages(json, out var usages, out var usagesError))
                    return Task.FromResult(ErrorJSON(Request, HTTPStatusCode.BadRequest, usagesError));

                if (!EMSP.Certificates.SetUsages(handle, usages, out _, out var usagesRefused))
                    return Task.FromResult(ErrorJSON(Request, HTTPStatusCode.BadRequest, usagesRefused));

            }

            return Task.FromResult(
                       JSONResponse(Request, HTTPStatusCode.OK,
                                    EMSP.Certificates.Get(handle)!.ToJSON(WithDiagnostics: true))
                   );

        }

        /// <summary>
        /// DELETE /api/v1/certificates/{id}: take a certificate out of the
        /// store and delete its file.
        /// </summary>
        /// <remarks>
        /// Nothing on an EMSP names a certificate of its store by its handle,
        /// so nothing is left pointing at a file that is gone: a server held to
        /// a certificate is held to its fingerprint, which its entry in the
        /// configuration keeps.
        /// </remarks>
        private Task<HTTPResponse> DeleteCertificate(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Edit(NodeResources.Certificates), true, out _, out var refused))
                return Task.FromResult(refused);

            if (!EMSP.Certificates.Remove(HandleOf(Request), out var error))
                return Task.FromResult(ErrorJSON(Request, HTTPStatusCode.NotFound, error));

            return Task.FromResult(
                       JSONResponse(Request, HTTPStatusCode.OK, EMSP.CertificatesJSON())
                   );

        }

        #endregion

        #region (private) PostCertificateReload(Request)

        /// <summary>
        /// POST /api/v1/certificates/reload: read the store directory again.
        /// </summary>
        /// <remarks>
        /// What the store does at every start, on demand: certificates somebody
        /// copied into the directory are adopted, and entries whose files are
        /// gone are dropped. It exists because putting a file in a directory is
        /// a perfectly good way to install a certificate on a machine somebody
        /// already has a shell on, and having to restart the EMSP to be
        /// noticed would make it a worse one.
        /// </remarks>
        private Task<HTTPResponse> PostCertificateReload(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Edit(NodeResources.Certificates), true, out _, out var refused))
                return Task.FromResult(refused);

            EMSP.Certificates.Reload();

            return Task.FromResult(
                       JSONResponse(Request, HTTPStatusCode.OK, EMSP.CertificatesJSON())
                   );

        }

        #endregion

        #region (private static) TryReadUsages(JSON, out Usages, out Error)

        /// <summary>
        /// The "usages" of a request: absent or null for every use, or a list
        /// of usages - or why not.
        /// </summary>
        /// <remarks>
        /// Whether each of them is a usage the store knows is the store's to
        /// say, and it says so in a sentence that names the ones it knows.
        /// </remarks>
        private static Boolean TryReadUsages(JObject                           JSON,
                                             out IReadOnlyList<String>?        Usages,
                                             [NotNullWhen(false)] out String?  Error)
        {

            Usages  = null;
            Error   = null;

            if (!JSON.TryGetValue("usages", out var token) || token.Type == JTokenType.Null)
                return true;

            if (token is not JArray array || array.Any(usage => usage.Type != JTokenType.String))
            {
                Error = "'usages' has to be a list of usages, such as [\"dns\", \"nts\"], or null for every use.";
                return false;
            }

            Usages = [.. array.Select(usage => usage.Value<String>()!)];
            return true;

        }

        #endregion

        #region (private static) HandleOf(Request)

        /// <summary>
        /// The certificate handle out of the request's path.
        /// </summary>
        private static String HandleOf(HTTPRequest Request)

            => Request.ParsedURLParameters.Length > 0
                   ? Request.ParsedURLParameters[0].Trim()
                   : "";

        #endregion

    }

}
