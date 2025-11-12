using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;

namespace SecureExam.Core.Security
{
    public class TotpManager
    {
        private const int TimeStep = 30; // 30 seconds
        private const int CodeDigits = 6;

        public static string GenerateSecret()
        {
            byte[] buffer = new byte[20];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(buffer);
            }
            return Base32Encode(buffer);
        }

        public static string GenerateCode(string secret)
        {
            byte[] key = Base32Decode(secret);
            long counter = GetCurrentCounter();
            return GenerateHOTP(key, counter);
        }

        public static bool ValidateCode(string secret, string code, int windowSize = 1)
        {
            byte[] key = Base32Decode(secret);
            long currentCounter = GetCurrentCounter();

            // Check current time window and adjacent windows
            for (int i = -windowSize; i <= windowSize; i++)
            {
                string expectedCode = GenerateHOTP(key, currentCounter + i);
                if (expectedCode == code)
                {
                    return true;
                }
            }
            return false;
        }

        public static int GetRemainingSeconds()
        {
            long unixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return TimeStep - (int)(unixTime % TimeStep);
        }

        private static long GetCurrentCounter()
        {
            long unixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return unixTime / TimeStep;
        }

        private static string GenerateHOTP(byte[] key, long counter)
        {
            byte[] counterBytes = BitConverter.GetBytes(counter);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(counterBytes);
            }

            using (var hmac = new HMACSHA1(key))
            {
                byte[] hash = hmac.ComputeHash(counterBytes);
                int offset = hash[hash.Length - 1] & 0x0F;

                int binary = ((hash[offset] & 0x7F) << 24)
                    | ((hash[offset + 1] & 0xFF) << 16)
                    | ((hash[offset + 2] & 0xFF) << 8)
                    | (hash[offset + 3] & 0xFF);

                int otp = binary % (int)Math.Pow(10, CodeDigits);
                return otp.ToString().PadLeft(CodeDigits, '0');
            }
        }

        private static string Base32Encode(byte[] data)
        {
            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
            StringBuilder result = new StringBuilder();

            for (int i = 0; i < data.Length; i += 5)
            {
                int byteCount = Math.Min(5, data.Length - i);
                ulong buffer = 0;

                for (int j = 0; j < byteCount; j++)
                {
                    buffer = (buffer << 8) | data[i + j];
                }

                int bitCount = byteCount * 8;
                while (bitCount > 0)
                {
                    int index = (bitCount >= 5)
                        ? (int)(buffer >> (bitCount - 5)) & 0x1F
                        : (int)(buffer & (ulong)((1 << bitCount) - 1)) << (5 - bitCount);
                    result.Append(alphabet[index]);
                    bitCount -= 5;
                }
            }

            return result.ToString();
        }

        private static byte[] Base32Decode(string encoded)
        {
            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
            encoded = encoded.ToUpper().Replace(" ", "").Replace("-", "");

            byte[] result = new byte[encoded.Length * 5 / 8];
            int buffer = 0;
            int bitsLeft = 0;
            int index = 0;

            foreach (char c in encoded)
            {
                int value = alphabet.IndexOf(c);
                if (value < 0) continue;

                buffer = (buffer << 5) | value;
                bitsLeft += 5;

                if (bitsLeft >= 8)
                {
                    result[index++] = (byte)(buffer >> (bitsLeft - 8));
                    bitsLeft -= 8;
                }
            }

            return result;
        }
    }
}

