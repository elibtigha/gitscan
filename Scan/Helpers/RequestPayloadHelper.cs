using Octokit;
using Octokit.Internal;
using System;
using System.Security.Cryptography;

namespace ScanXGitHubApp
{
    public static class RequestPayloadHelper
    {
        static readonly Lazy<IJsonSerializer> serializer = new Lazy<IJsonSerializer>(
            () =>
            {
                return new SimpleJsonSerializer();
            });

        static readonly string messageSignaturePrefix = "sha1=";

        public static CheckSuiteEventPayload Parse(string payload)
        {
            if (string.IsNullOrEmpty(payload))
            {
                throw new ArgumentException(nameof(payload));
            }

            return serializer.Value.Deserialize<CheckSuiteEventPayload>(payload);
        }

        public static T Parse<T>(string payload)
        {
            if (string.IsNullOrEmpty(payload))
            {
                throw new ArgumentException(nameof(payload));
            }

            return serializer.Value.Deserialize<T>(payload);
        }


        public static bool ValidateSender(string message, string messageSignature, string key)
        {
            System.Text.ASCIIEncoding encoding = new System.Text.ASCIIEncoding();
            byte[] keyByte = encoding.GetBytes(key);
            using (HMACSHA1 hmacsha1 = new HMACSHA1(keyByte))
            { 
                byte[] hashmessage = hmacsha1.ComputeHash(encoding.GetBytes(message));
            string hmacText = $"{messageSignaturePrefix}{ByteToString(hashmessage)}";
            return messageSignature.Equals(hmacText, StringComparison.OrdinalIgnoreCase);
                }
        }

        public static string ByteToString(byte[] buff)
        {
            string sbinary = string.Empty;
            for (int i = 0; i < buff.Length; i++)
            {
                sbinary += buff[i].ToString("X2"); // hex format
            }
            return (sbinary);
        }
    }
}
