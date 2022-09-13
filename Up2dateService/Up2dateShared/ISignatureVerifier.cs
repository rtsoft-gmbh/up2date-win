﻿using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;

namespace Up2dateShared
{
    public enum SignatureVerificationLevel
    {
        SignedByAnyCertificate,
        SignedByTrustedCertificate,
        SignedByWhitelistedCertificate
    }

    public interface ISignatureVerifier
    {
        bool VerifySignature(string file, SignatureVerificationLevel level);
        bool VerifySignature(X509Certificate2 certificate, SignatureVerificationLevel level);
        IList<X509Certificate2> GetWhitelistedCertificates();
        void RemoveCertificateFromWhilelist(X509Certificate2 certificate);
        bool IsCertificateValidAndTrusted(string certificateFilePath);
        Result AddCertificateToWhitelist(X509Certificate2 certificate);
        Result AddCertificateToWhitelist(string certificateFilePath);
    }
}
