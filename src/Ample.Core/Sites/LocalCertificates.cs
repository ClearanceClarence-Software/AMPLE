using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Ample.Core;
using Ample.Stack;

namespace Ample.Sites;

/// <summary>
/// A certificate authority that exists only on this computer, and the server certificate it issues for localhost
/// and every HTTPS project. Once Windows trusts the authority, browsers accept the sites without a warning.
/// </summary>
public static class LocalCertificates
{
    private const string AuthorityName = "AMPLE Local CA";

    /// <summary>The authority with its private key, in the settings folder; it never leaves this computer.</summary>
    public static string AuthorityPfxPath => Path.Combine(AppSettings.Folder, "local-ca.pfx");

    /// <summary>The authority's public certificate, for importing into Firefox or another computer's browser.</summary>
    public static string AuthorityCrtPath => Path.Combine(AppSettings.Folder, "local-ca.crt");

    /// <summary>Full path of the server certificate file Apache serves.</summary>
    /// <param name="paths">The stack.</param>
    /// <returns>etc\apache\ssl\ample-local.crt; the file may not exist.</returns>
    public static string ServerCertificatePath(AmplePaths paths) =>
        Path.Combine(paths.EtcApache, ApacheConfig.ServerCertificate.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Full path of the server key file.</summary>
    /// <param name="paths">The stack.</param>
    /// <returns>etc\apache\ssl\ample-local.key; the file may not exist.</returns>
    public static string ServerKeyPath(AmplePaths paths) =>
        Path.Combine(paths.EtcApache, ApacheConfig.ServerKey.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Loads the authority, creating it the first time.</summary>
    /// <returns>The authority with its private key; the caller disposes it.</returns>
    /// <exception cref="CryptographicException">The stored authority file is damaged.</exception>
    /// <exception cref="IOException">The settings folder cannot be written.</exception>
    public static X509Certificate2 LoadOrCreateAuthority()
    {
        if (!File.Exists(AuthorityPfxPath))
        {
            CreateAuthority();
        }

        // Ephemeral keys stay in memory, so loading the authority leaves no key containers behind in Windows.
        return X509CertificateLoader.LoadPkcs12FromFile(AuthorityPfxPath, null, X509KeyStorageFlags.EphemeralKeySet);
    }

    /// <summary>Creates a new authority, valid for ten years, and saves it in the settings folder.</summary>
    private static void CreateAuthority()
    {
        using RSA authorityKey = RSA.Create(3072);
        var subjectBuilder = new X500DistinguishedNameBuilder();
        subjectBuilder.AddOrganizationName("ClearanceClarence");
        subjectBuilder.AddOrganizationalUnitName($"{Environment.UserName}@{Environment.MachineName}");
        subjectBuilder.AddCommonName(AuthorityName);
        var request = new CertificateRequest(
            subjectBuilder.Build(),
            authorityKey,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        DateTimeOffset now = DateTimeOffset.UtcNow;
        using X509Certificate2 authority = request.CreateSelfSigned(now.AddDays(-1), now.AddYears(10));

        Directory.CreateDirectory(AppSettings.Folder);
        File.WriteAllBytes(AuthorityPfxPath, authority.Export(X509ContentType.Pkcs12));
        File.WriteAllText(AuthorityCrtPath, authority.ExportCertificatePem() + "\n");
    }

    /// <summary>Whether Windows already trusts the authority for the current user.</summary>
    /// <param name="authority">The authority from <see cref="LoadOrCreateAuthority"/>.</param>
    /// <returns>True when its certificate is in the current user's Trusted Root store.</returns>
    public static bool IsTrusted(X509Certificate2 authority)
    {
        using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly);
        X509Certificate2Collection storedCertificates = store.Certificates;
        X509Certificate2Collection matches =
            storedCertificates.Find(X509FindType.FindByThumbprint, authority.Thumbprint, false);
        bool isTrusted = matches.Count > 0;

        // Each collection holds its own copies, and certificates hold native handles until disposed.
        foreach (X509Certificate2 certificate in storedCertificates.Concat(matches))
        {
            certificate.Dispose();
        }
        return isTrusted;
    }

    /// <summary>
    /// Adds the authority to the current user's Trusted Root store. Windows asks the user to confirm with its own
    /// security warning; no admin rights are needed.
    /// </summary>
    /// <param name="authority">The authority; only its public part is added.</param>
    /// <returns>True when it was added or was already there; false when the user answered No.</returns>
    public static bool Trust(X509Certificate2 authority)
    {
        using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);
        using X509Certificate2 publicPart = X509CertificateLoader.LoadCertificate(authority.RawData);
        try
        {
            store.Add(publicPart);
            return true;
        }
        catch (CryptographicException)
        {
            // Windows throws when the user answers No to its "install this certificate?" warning.
            return false;
        }
    }

    /// <summary>
    /// Issues a server certificate for localhost and the given host names and writes it, with its key, where Apache
    /// reads it.
    /// </summary>
    /// <param name="authority">The authority, with its private key.</param>
    /// <param name="hostNames">The HTTPS projects' host names; localhost, 127.0.0.1 and ::1 are always
    /// included.</param>
    /// <param name="paths">The stack; etc\apache\ssl is created when missing.</param>
    public static void IssueServerCertificate(
        X509Certificate2 authority,
        IEnumerable<string> hostNames,
        AmplePaths paths)
    {
        using RSA serverKey = RSA.Create(2048);
        var request = new CertificateRequest(
            new X500DistinguishedName("CN=localhost, O=AMPLE"),
            serverKey,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
            true));
        var serverAuthentication = new OidCollection { new Oid("1.3.6.1.5.5.7.3.1", "Server Authentication") };
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(serverAuthentication, false));
        request.CertificateExtensions.Add(SubjectAlternativeNames(hostNames));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        request.CertificateExtensions.Add(
            X509AuthorityKeyIdentifierExtension.CreateFromCertificate(authority, true, false));

        // Browsers only accept a positive serial number, so the top bit of the first byte is cleared.
        byte[] serialNumber = RandomNumberGenerator.GetBytes(16);
        serialNumber[0] &= 0x7F;

        DateTimeOffset notBefore = DateTimeOffset.UtcNow.AddHours(-1);
        DateTimeOffset notAfter = Min(notBefore.AddDays(825), new DateTimeOffset(authority.NotAfter).AddDays(-1));
        using X509Certificate2 server = request.Create(authority, notBefore, notAfter, serialNumber);

        Directory.CreateDirectory(paths.EtcApacheSsl);
        // The authority follows the server certificate so clients that do not know it yet still get the full chain.
        string chain = server.ExportCertificatePem() + "\n" + authority.ExportCertificatePem() + "\n";
        File.WriteAllText(ServerCertificatePath(paths), chain);
        File.WriteAllText(ServerKeyPath(paths), serverKey.ExportPkcs8PrivateKeyPem() + "\n");
    }

    /// <summary>Issues the server certificate for localhost and the given hosts, reporting failures as text.</summary>
    /// <param name="paths">The stack.</param>
    /// <param name="hostNames">The HTTPS projects' host names.</param>
    /// <param name="problem">Receives why it failed; null on success.</param>
    /// <returns>True when the certificate and key are in place.</returns>
    public static bool EnsureServerCertificate(AmplePaths paths, IReadOnlyList<string> hostNames, out string? problem)
    {
        try
        {
            using X509Certificate2 authority = LoadOrCreateAuthority();
            IssueServerCertificate(authority, hostNames, paths);
            problem = null;
            return true;
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        {
            problem = ex.Message;
            return false;
        }
    }

    /// <summary>The subject alternative names: localhost, both loopback addresses, then each project.</summary>
    private static X509Extension SubjectAlternativeNames(IEnumerable<string> hostNames)
    {
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        names.AddIpAddress(IPAddress.Loopback);
        names.AddIpAddress(IPAddress.IPv6Loopback);
        foreach (string hostName in hostNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!hostName.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            {
                names.AddDnsName(hostName);
            }
        }
        return names.Build();
    }

    private static DateTimeOffset Min(DateTimeOffset first, DateTimeOffset second) => first < second ? first : second;
}
