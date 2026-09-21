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

using Newtonsoft.Json.Linq;

#endregion

namespace cloud.charging.open.EMSP.Contracts
{

    /// <summary>
    /// What came of issuing or taking back a contract: whether it worked,
    /// one sentence about it, and what the web interface should show.
    /// </summary>
    /// <param name="Success">Whether it worked.</param>
    /// <param name="Message">One sentence about it, for the page.</param>
    /// <param name="Data">What the page should show, e.g. the certificate that was just issued.</param>
    public sealed record ContractOperationResult(Boolean   Success,
                                                 String    Message,
                                                 JObject?  Data   = null)
    {

        public static ContractOperationResult Ok    (String Message, JObject? Data = null)
            => new (true,  Message, Data);

        public static ContractOperationResult Failed(String Message)
            => new (false, Message);

    }

}
