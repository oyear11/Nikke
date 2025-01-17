﻿/*
* MIT License
* 
* Copyright (c) 2022 Razmoth
* 
* Permission is hereby granted, free of charge, to any person obtaining a copy
* of this software and associated documentation files (the "Software"), to deal
* in the Software without restriction, including without limitation the rights
* to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
* copies of the Software, and to permit persons to whom the Software is
* furnished to do so, subject to the following conditions:
* 
* The above copyright notice and this permission notice shall be included in all
* copies or substantial portions of the Software.
* 
* THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
* IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
* FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
* AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
* LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
* OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
* SOFTWARE.
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Nikke_NKAB_Decrypter
{
    public static class Decrypter
    {
        private static readonly int MAGIC_BYTES = 0x42414B4E;
        private static readonly int VERSION = 3;

        // https://github.com/Razmoth/Nikke/blob/main/Nikke.py
        private static readonly Dictionary<int, string> KEYS = new Dictionary<int, string>()
            {
                { 0, "BD0231EDD70676A74EEC8A775CA2770B593B5328557C473FA7F33E0887FB379E" },
                { 1, "599D714FF8F3F88986F99A1B775F107C59016A123BB7671DDF175DC2A3409660" },
                { 2, "4AA3DE4DC89A1A55E7016DCC753B2514B026AFFF4F0AF82758D05CA81E0E0B69" },
                { 3, "F6EAAAE08E8C99C86B9C8A6CA33DD9A46E0CDDDFD67F334E8A3289425B5AFB7F" },
                { 4, "5C591C5F700C860D97C0C28F7026947561ECEFB80081BB47A8DF4571D9978B5B" }
            };
        private class BundleHeaderParam
        {
            public int Version { get; set; }
            public int KeyVersion { get; set; }
            public int KeyIndex { get; set; }
            public int ObfuscateValue { get; set; }
            public int HeaderSize { get; set; }
            public int EncryptMode { get; set; }
            public int EncryptFlags { get; set; }
            public int BlockCount { get; set; }
            public byte[] IV { get; set; }
            public byte[] KeyB { get; set; }
            public BundleHeaderParam(int ver, int keyVer, int keyIndex, int obfuscateValue, int headerSize, int encryptMode, int encryptFlags, int blockCount, byte[] iv, byte[] keyB)
            {
                Version = ver;
                KeyVersion = keyVer;
                KeyIndex = keyIndex;
                ObfuscateValue = obfuscateValue;
                HeaderSize = headerSize;
                EncryptMode = encryptMode;
                EncryptFlags = encryptFlags;
                BlockCount = blockCount;
                IV = iv;
                KeyB = keyB;
            }
        }
        private static byte[] StringToByteArray(string hex)
        {
            return Enumerable.Range(0, hex.Length)
                             .Where(x => x % 2 == 0)
                             .Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
                             .ToArray();
        }
        private static bool IsEncrypted(int value, int offset)
        {
            int mask = 0x8000 >> offset;
            return (value & mask) != 0;
        }
        static byte[] PerformCryptography(byte[] data, ICryptoTransform cryptoTransform)
        {
            using (var ms = new MemoryStream())
            using (var cryptoStream = new CryptoStream(ms, cryptoTransform, CryptoStreamMode.Write))
            {
                cryptoStream.Write(data, 0, data.Length);
                cryptoStream.FlushFinalBlock();

                return ms.ToArray();
            }
        }
        private static BundleHeaderParam ReadBundleHeader(ref BinaryReader br)
        {
            int magic = br.ReadInt32();
            int version = br.ReadInt32();
            byte[] iv = br.ReadBytes(0x10);
            long pos = br.BaseStream.Position;
            br.BaseStream.Seek(-2, SeekOrigin.End);
            short obfuscateValue = br.ReadInt16();
            br.BaseStream.Position = pos;
            int headerSize = (br.ReadInt16() + obfuscateValue) & 0xFFFF;
            int keyVersion = (br.ReadInt16() + obfuscateValue) & 0xFFFF;
            int keyIndex = (br.ReadInt16() + obfuscateValue) & 0xFFFF;
            int encryptMode = (br.ReadInt16() + obfuscateValue) & 0xFFFF;
            int encryptFlags = (br.ReadInt16() + obfuscateValue) & 0xFFFF;
            int blockCount = (br.ReadInt16() + obfuscateValue) & 0xFFFF;
            byte[] keyB = br.ReadBytes(0x20);
            var header = new BundleHeaderParam(version, keyVersion, keyIndex, obfuscateValue, headerSize, encryptMode, encryptFlags, blockCount, iv, keyB);
            return header;
        }
        private static byte[] GenerateByteArray(int size)
        {
            Random rnd = new Random();
            byte[] b = new byte[size];
            rnd.NextBytes(b);
            return b;
        }

        static void EncryptBlock(ref BinaryWriter bw, ref BinaryReader br, ICryptoTransform encrypter, int flags, int size, int index = 0)
        {
            for (int i = 0; i < size; i += 0x10)
            {
                byte[] blockData = br.ReadBytes(0x10);
                if (IsEncrypted(flags, index))
                {
                    byte[] blockEncrypted = PerformCryptography(blockData, encrypter);
                    bw.Write(blockEncrypted);
                }
                else
                {
                    bw.Write(blockData);
                }
                index = (index + 1) % 0x10;
            }
        }
        public static void EncryptV3(string input, string output)
        {
            var ms = new MemoryStream();
            var bw = new BinaryWriter(ms);
            var br = new BinaryReader(File.OpenRead(input));
            HashAlgorithm hash = SHA256.Create();
            bw.Write(MAGIC_BYTES);
            bw.Write(VERSION);
            bw.BaseStream.Position += 0x10;
            Random rd = new Random();
            int obfuscateValue = rd.Next(0, 0xFF);
            int flags = rd.Next(0xFF, 0xFFFF);
            br.BaseStream.Seek(0, SeekOrigin.Begin);
            bw.Write((short)(0x3C - obfuscateValue));
            bw.Write((short)(obfuscateValue * -1));
            bw.Write((short)(obfuscateValue * -1));
            int keyIndex = rd.Next(0, 4);
            bw.Write((short)(keyIndex - obfuscateValue));
            bw.Write((short)(flags - obfuscateValue));
            bw.Write((short)(obfuscateValue * -1));
            byte[] keyB = GenerateByteArray(0x20);
            bw.Write(keyB);
            byte[] keyA = StringToByteArray(KEYS[keyIndex]);
            byte[] key = hash.ComputeHash(keyA.Concat(keyB).ToArray());
            var aes = new AesManaged
            {
                Key = key,
                Mode = CipherMode.ECB,
                Padding = PaddingMode.Zeros
            };
            ICryptoTransform encryptor = aes.CreateEncryptor();
            EncryptBlock(ref bw, ref br, encryptor, flags, (int)br.BaseStream.Length);
            bw.Write((short)obfuscateValue);
            br.Close();
            bw.Close();
            File.WriteAllBytes(output, ms.ToArray());
        }
        private static void DecryptBlock(ref BinaryWriter bw, ref BinaryReader br, ICryptoTransform decrypter, int flags, int size, int index = 0)
        {
            for (int i = 0; i < size; i += 0x10)
            {

                byte[] blockData = br.ReadBytes(0x10);
                if (IsEncrypted(flags, index))
                {
                    byte[] blockDecrypted = decrypter.TransformFinalBlock(blockData, 0, 0x10);
                    bw.Write(blockDecrypted);
                }
                else
                {
                    bw.Write(blockData);
                }
                index = (index + 1) % 0x10;
            }
        }
        public static void DecryptV3(string input, string output)
        {
            var br = new BinaryReader(File.OpenRead(input));
            var header = ReadBundleHeader(ref br);
            if (header.Version != VERSION) return;
            int headerBlockCount = header.BlockCount >> 8;
            int footerBlockCount = header.BlockCount & 0xFF;
            int decryptedSize = (int)(br.BaseStream.Length - br.BaseStream.Position);
            int dataOffset = (int)br.BaseStream.Position;
            if (header.KeyIndex >= KEYS.Count) return;
            byte[] keyA = StringToByteArray(KEYS[header.KeyIndex]);

            HashAlgorithm hash = SHA256.Create();
            byte[] key = hash.ComputeHash(keyA.Concat(header.KeyB).ToArray());
            var aes = new AesManaged
            {
                Key = key,
                IV = header.IV,
                Mode = header.EncryptMode == 1 ? CipherMode.ECB : CipherMode.CBC,
                Padding = PaddingMode.Zeros
            };
            ICryptoTransform decrypter = aes.CreateDecryptor();
            var decrypted = new MemoryStream();
            var bw = new BinaryWriter(decrypted);
            if (headerBlockCount == 0 && footerBlockCount == 0)
            {
                int encryptedSize = 0x10 * (int)Math.Floor((decimal)decryptedSize / 0x10);
                DecryptBlock(ref bw, ref br, decrypter, header.EncryptFlags, encryptedSize);
            }
            else
            {
                int encryptedHeaderSize = 0x10 * Math.Min(headerBlockCount * 0x10, (int)Math.Floor((decimal)decryptedSize / 0x10));
                int encryptedFooterSize = 0x10 * Math.Min(footerBlockCount * 0x10, (int)Math.Floor((decimal)(decryptedSize - encryptedHeaderSize) / 0x10));
                if (encryptedHeaderSize > 0)
                {
                    DecryptBlock(ref bw, ref br, decrypter, header.EncryptFlags, encryptedHeaderSize);
                }
                if (encryptedFooterSize > 0)
                {
                    int encryptedFooterOffset = decryptedSize - encryptedFooterSize;
                    bw.Write(br.ReadBytes(encryptedFooterOffset - (int)br.BaseStream.Position + dataOffset));
                    int blockIndex = (int)Math.Floor((double)(encryptedFooterOffset % (0x10 * 0x10) / 0x10));
                    DecryptBlock(ref bw, ref br, decrypter, header.EncryptFlags, encryptedFooterSize, blockIndex);
                }
            }
            br.Close();
            bw.Close();
            File.WriteAllBytes(output, decrypted.ToArray());
        }

        // Old files
        /*public static void Decrypt(string input, string output)
        {
            var result = new MemoryStream();
            var stream = File.OpenRead(input);
            var br = new BinaryReader(stream);
            string sig = Encoding.ASCII.GetString(br.ReadBytes(4));
            if (sig != "NKAB") return;
            int version = br.ReadInt32();
            byte[] key, iv, encrypted, body;
            if (version == 1)
            {
                br.BaseStream.Position = 0xC;
                int keyLen = br.ReadInt16() + 0x64;
                int encryptedLen = br.ReadInt16() + 0x64;
                byte[] keyHash = br.ReadBytes(keyLen);
                iv = br.ReadBytes(0x10);
                encrypted = br.ReadBytes(encryptedLen);
                HashAlgorithm hash = SHA256.Create();
                key = hash.ComputeHash(keyHash);
                body = br.ReadBytes((int)(br.BaseStream.Length - br.BaseStream.Position));
            }
            else if (version == 2)
            {
                br.BaseStream.Position = br.BaseStream.Length - 0x20;
                int num = br.ReadInt16();
                br.BaseStream.Position = 0xC;
                int keyLen = br.ReadInt16() + num;
                if (keyLen < 0)
                {
                    br.BaseStream.Position -= 2;
                    keyLen = br.ReadUInt16() + num;
                }
                int encryptedLen = br.ReadInt16() + num;
                if (encryptedLen < 0)
                {
                    br.BaseStream.Position -= 2;
                    encryptedLen = br.ReadUInt16() + num;
                }
                iv = br.ReadBytes(0x10);
                encrypted = br.ReadBytes(encryptedLen);
                body = br.ReadBytes((int)(br.BaseStream.Length - br.BaseStream.Position - 0x20));
                key = br.ReadBytes(keyLen);
            }
            else return;
            var aes = new AesManaged
            {
                Key = key,
                IV = iv,
                Mode = CipherMode.CBC,
                Padding = PaddingMode.Zeros
            };
            ICryptoTransform decrypter = aes.CreateDecryptor();
            byte[] decrypted = decrypter.TransformFinalBlock(encrypted, 0, encrypted.Length);
            using (var bw = new BinaryWriter(result))
            {
                bw.Write(decrypted);
                bw.Write(body);
            }
            br.Close();
 
            File.WriteAllBytes(Path.Combine(output, Path.GetFileName(input)), result.ToArray());
        }*/


    }
}
