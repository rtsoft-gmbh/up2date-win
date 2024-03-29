﻿using System.Collections.Generic;

namespace Up2dateShared
{
    public interface ISettingsManager
    {
        string ProvisioningUrl { get; set; }
        string XApigToken { get; }
        string RequestCertificateUrl { get; set; }
        string CertificateThumbprint { get; set; }
        List<string> PackageExtensionFilterList { get; set; }
        bool CheckSignature { get; set; }
        SignatureVerificationLevel SignatureVerificationLevel { get; set; }
        string PackageInProgress { get; set; }
        string DefaultChocoSources { get; set; }
        bool RequiresConfirmationBeforeInstall { get; set; }
        string HawkbitUrl { get; set; }
        string DeviceId { get; set; }
        string SecurityToken { get; set; }
        bool SecureAuthorizationMode { get; set; }
    }
}