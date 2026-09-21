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

namespace cloud.charging.open.EMSP.Contracts
{

    /// <summary>
    /// Writing a file that its owner alone may read: a private key.
    /// </summary>
    /// <remarks>
    /// On POSIX the file is created with mode 0600 before a byte is written
    /// to it, so that there is no moment at which it lies readable by
    /// everybody. Windows has no equivalent of 0600 that is worth writing
    /// here - the directory decides there - so this is a plain write on
    /// Windows, and says so rather than pretending otherwise.
    /// </remarks>
    internal static class OwnerOnlyFile
    {

        #region Write(Path, Content)

        /// <summary>
        /// Write text readable and writable by its owner alone.
        /// </summary>
        public static void Write(String  Path,
                                 String  Content)
        {

            if (OperatingSystem.IsWindows())
            {
                File.WriteAllText(Path, Content);
                return;
            }

            using var stream = File.Open(
                                   Path,
                                   new FileStreamOptions {
                                       Mode            = FileMode.Create,
                                       Access          = FileAccess.Write,
                                       UnixCreateMode  = UnixFileMode.UserRead | UnixFileMode.UserWrite
                                   }
                               );

            using var writer = new StreamWriter(stream);

            writer.Write(Content);

        }

        #endregion

    }

}
