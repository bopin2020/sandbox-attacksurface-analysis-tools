﻿//  Copyright 2022 Google LLC. All Rights Reserved.
//
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
//
//  http://www.apache.org/licenses/LICENSE-2.0
//
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//  limitations under the License.

using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NtApiDotNet.Win32.Security.Authentication.Kerberos.Builder
{
    /// <summary>
    /// Class to represent a builder for a PAC entry.
    /// </summary>
    public sealed class KerberosAuthorizationDataPACBuilder : KerberosAuthorizationDataBuilder
    {
        /// <summary>
        /// The list of PAC entries.
        /// </summary>
        public List<KerberosAuthorizationDataPACEntryBuilder> Entries { get; }

        /// <summary>
        /// The PAC version. Should usually be 0.
        /// </summary>
        public int Version { get; set; }

        /// <summary>
        /// Constructor.
        /// </summary>
        public KerberosAuthorizationDataPACBuilder() : base(KerberosAuthorizationDataType.AD_WIN2K_PAC)
        {
            Entries = new List<KerberosAuthorizationDataPACEntryBuilder>();
        }

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="entries">The list of entries in the PAC.</param>
        /// <param name="version">The PAC version. Typically is 0.</param>
        public KerberosAuthorizationDataPACBuilder(int version, IEnumerable<KerberosAuthorizationDataPACEntry> entries) 
            : this()
        {
            Version = version;
            Entries.AddRange(entries.Select(e => e.ToBuilder()));
        }

        /// <summary>
        /// Resign the PAC's server checksum.
        /// </summary>
        /// <param name="key">The authentication key to use for the signature.</param>
        public void ResignForServer(KerberosAuthenticationKey key)
        {
            List<KerberosAuthorizationDataPACEntryBuilder> entries = new List<KerberosAuthorizationDataPACEntryBuilder>(Entries);
            KerberosAuthorizationDataPACSignatureBuilder server_checksum = null;
            for (int i = 0; i < entries.Count; ++i)
            {
                if (entries[i] is KerberosAuthorizationDataPACSignatureBuilder sig_builder)
                {
                    entries[i] = new KerberosAuthorizationDataPACSignatureBuilder(sig_builder.PACType,
                        sig_builder.SignatureType, new byte[sig_builder.Signature.Length], sig_builder.RODCIdentifier);
                    if (sig_builder.PACType == KerberosAuthorizationDataPACEntryType.ServerChecksum)
                        server_checksum = sig_builder;
                }
            }
            // No need to do anything if no server checksum.
            if (server_checksum == null)
                return;

            server_checksum.Signature = KerberosChecksum.Create(key, Encode(Version, entries), 
                KerberosKeyUsage.KerbNonKerbChksumSalt).Checksum;
        }

        /// <summary>
        /// Create the Kerberos PAC.
        /// </summary>
        /// <returns>The kerberos PAC.</returns>
        public override KerberosAuthorizationData Create()
        {
            if (!KerberosAuthorizationDataPAC.Parse(Encode(Version, Entries), out KerberosAuthorizationDataPAC auth_data))
                throw new InvalidDataException("PAC is invalid.");
            return auth_data;
        }

        private static byte[] Encode(int version, ICollection<KerberosAuthorizationDataPACEntryBuilder> entries)
        {
            MemoryStream stream = new MemoryStream();
            BinaryWriter writer = new BinaryWriter(stream);

            // Based on code form monoxgas.
            writer.Write(entries.Count);
            writer.Write(version);

            // sizeof(PACTYPE) + sizeof(PAC_INFO_BUFFER) * EntryCount
            long offset = 8 + 16 * entries.Count;

            foreach (var entry in entries.Select(e => e.Create()))
            {
                var entryData = entry.Data;

                // Write the PAC_INFO_BUFFER
                writer.Write((int)entry.PACType);
                writer.Write(entryData.Length);
                writer.Write(offset);

                // Write the actual data
                int current = (int)writer.BaseStream.Position;
                writer.BaseStream.Position = offset;
                writer.Write(entryData);
                offset = (offset + entryData.Length + 7) / 8 * 8;
                while (writer.BaseStream.Position < offset)
                {
                    // MS always rounds data boundaries
                    writer.Write('\x00');
                }
                writer.BaseStream.Position = current;
            }
            return stream.ToArray();
        }
    }
}
