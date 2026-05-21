using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Orders;
using Nop.Services.Common;
using Nop.Services.Directory;
using Nop.Services.Installation;

namespace Nevoweb.ETransactions.Services;

public class ETransactionsRequestBuilder
{
    private readonly IAddressService _addressService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICountryService _countryService;
    private readonly ETransactionsPaymentSettings _settings;

    public ETransactionsRequestBuilder(IAddressService addressService,
        IHttpClientFactory httpClientFactory,
        ICountryService countryService,
        ETransactionsPaymentSettings settings)
    {
        _addressService = addressService;
        _httpClientFactory = httpClientFactory;
        _countryService = countryService;
        _settings = settings;
    }

    public async Task<(string postUrl, Dictionary<string, string> values)> BuildPostAsync(Order order, string effectueUrl, string refuseUrl, string annuleUrl, string notifyUrl)
    {
        var billingAddress = await _addressService.GetAddressByIdAsync(order.BillingAddressId);
        var amount = ((int)Math.Round(order.OrderTotal * 100m, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);
        var pbxTime = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

        var values = new Dictionary<string, string>
        {
            ["PBX_SITE"] = _settings.PbxSite,
            ["PBX_RANG"] = _settings.PbxRang,
            ["PBX_DEVISE"] = _settings.PbxDevise,
            ["PBX_TOTAL"] = amount,
            ["PBX_IDENTIFIANT"] = _settings.PbxIdentifiant,
            ["PBX_CMD"] = order.Id.ToString(CultureInfo.InvariantCulture),
            ["PBX_PORTEUR"] = billingAddress?.Email ?? string.Empty,
            ["PBX_EFFECTUE"] = effectueUrl,
            ["PBX_REFUSE"] = refuseUrl,
            ["PBX_ANNULE"] = annuleUrl,
            ["PBX_REPONDRE_A"] = notifyUrl,
            ["PBX_HASH"] = "SHA512",
            ["PBX_TIME"] = pbxTime,
            ["PBX_RETOUR"] = BuildRetourValue(),
            ["PBX_SHOPPINGCART"] = "<?xml version='1.0' encoding='utf-8'?><shoppingcart><total><totalQuantity>1</totalQuantity></total></shoppingcart>",
            ["PBX_BILLING"] = await BuildBillingXmlAsync(billingAddress)
        };

        if (_settings.ReserveOnly)
            values["PBX_AUTOSEULE"] = "O";

        // Disable 3DS in debug/preproduction mode so non-enrolled test cards (e.g. 4111111111111111) are accepted
        if (_settings.Preproduction && _settings.DebugMode)
            values["PBX_3DS"] = "N";

        values["PBX_HMAC"] = ComputeHmac(values, _settings.HmacKey);

        return (await GetPostUrlAsync(), values);
    }

    protected virtual string BuildRetourValue()
    {
        var value = string.IsNullOrWhiteSpace(_settings.PbxRetour)
            ? "ref:R;rtnerr:E;auto:A;trans:S;call:T"
            : _settings.PbxRetour.Trim();

        if (!value.Contains("ref:", StringComparison.OrdinalIgnoreCase))
            value += ";ref:R";
        if (!value.Contains("rtnerr:", StringComparison.OrdinalIgnoreCase))
            value += ";rtnerr:E";
        if (!value.Contains("auto:", StringComparison.OrdinalIgnoreCase))
            value += ";auto:A";
        if (!value.Contains("trans:", StringComparison.OrdinalIgnoreCase))
            value += ";trans:S";
        if (!value.Contains("call:", StringComparison.OrdinalIgnoreCase))
            value += ";call:T";

        // Always request RSA signature on IPN callback
        if (!value.Contains("sign:", StringComparison.OrdinalIgnoreCase))
            value += ";sign:K";

        return value.Trim(';');
    }

    /// <summary>
    /// Verifies the RSA-SHA1 signature sent by ETransactions on IPN callbacks.
    /// </summary>
    /// <param name="message">All IPN params except "sign", joined as key=value&amp;key=value in received order.</param>
    /// <param name="base64Signature">The raw value of the "sign" parameter (base64-encoded).</param>
    public virtual bool VerifyIpnSignature(string message, string base64Signature)
    {
        try
        {
            // Key files are in the etc/ folder alongside the plugin.
            // pubkey.pem          = RSA-1024, pre-production  (matches PHP module kit naming)
            // pubkey_RSA_2048.pem = RSA-2048, production      (matches PHP module kit naming)
            var pemFileName = _settings.Preproduction
                ? ETransactionsPaymentDefaults.IpnPublicKeyPreproductionFile
                : ETransactionsPaymentDefaults.IpnPublicKeyProductionFile;
            var pemPath = Path.Combine(AppContext.BaseDirectory, "Plugins", "Payments.ETransactions", "etc", pemFileName);
            var publicKeyPem = File.ReadAllText(pemPath);

            var signatureBytes = Convert.FromBase64String(base64Signature);
            var messageBytes = Encoding.UTF8.GetBytes(message);

            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem);

            return rsa.VerifyData(
                messageBytes,
                signatureBytes,
                HashAlgorithmName.SHA1,
                RSASignaturePadding.Pkcs1);
        }
        catch
        {
            return false;
        }
    }

    protected virtual async Task<string> BuildBillingXmlAsync(Address billingAddress)
    {
        if (billingAddress is null)
            return "<?xml version='1.0' encoding='utf-8'?><Billing />";

        var country = await _countryService.GetCountryByAddressAsync(billingAddress);
        var countryCode = country is null ? null : ISO3166.FromCountryCode(country.TwoLetterIsoCode);
        var numericCode = (countryCode?.NumericCode ?? 250).ToString("000", CultureInfo.InvariantCulture);
        var dialingCode = countryCode?.DialCodes?.FirstOrDefault() ?? "1";

        var firstName = CropAndSanitize(billingAddress.FirstName, 22);
        var lastName = CropAndSanitize(billingAddress.LastName, 22);
        var address1 = CropAndSanitize($"{billingAddress.Address1} {billingAddress.Address2}".Trim(), 50);
        var address2 = CropAndSanitize(billingAddress.Address2, 50);
        var city = CropAndSanitize(billingAddress.City, 50);
        var zip = CropAndSanitize(billingAddress.ZipPostalCode, 16);
        var phoneRaw = Regex.Replace(billingAddress.PhoneNumber ?? string.Empty, "[^0-9+]", string.Empty);
        var mobilePhone = CropAndSanitize(phoneRaw, 20, false);

        if (!string.IsNullOrWhiteSpace(dialingCode))
        {
            var normalizedDial = "+" + Regex.Replace(dialingCode, "[^0-9]", string.Empty);
            if (mobilePhone.StartsWith(normalizedDial, StringComparison.OrdinalIgnoreCase))
                mobilePhone = mobilePhone[normalizedDial.Length..];
            if (mobilePhone.StartsWith("+", StringComparison.OrdinalIgnoreCase))
                mobilePhone = mobilePhone[1..];
            if (string.IsNullOrWhiteSpace(mobilePhone))
                mobilePhone = "0123456789";
        }

        var xml = $"<?xml version='1.0' encoding='utf-8'?><Billing><Address><FirstName>{firstName}</FirstName><LastName>{lastName}</LastName><Address1>{address1}</Address1><Address2>{address2}</Address2><ZipCode>{zip}</ZipCode><City>{city}</City><CountryCode>{numericCode}</CountryCode><CountryCodeMobilePhone>+{Regex.Replace(dialingCode, "[^0-9]", string.Empty)}</CountryCodeMobilePhone><MobilePhone>{mobilePhone}</MobilePhone></Address></Billing>";

        return RemoveDiacritics(xml);
    }

    protected virtual async Task<string> GetPostUrlAsync()
    {
        if (_settings.Preproduction)
            return _settings.PreprodUrl;

        if (await IsMainEndpointAvailableAsync())
            return _settings.MainUrl;

        return _settings.BackupUrl;
    }

    protected virtual async Task<bool> IsMainEndpointAvailableAsync()
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(4);
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://tpeweb.paybox.com/load.html");
            using var response = await client.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    protected virtual string ComputeHmac(IReadOnlyDictionary<string, string> fields, string hexKey)
    {
        var message = string.Join("&", fields
            .Where(pair => pair.Key != "PBX_HMAC")
            .Select(pair => $"{pair.Key}={pair.Value}"));

        var keyBytes = PackHex(hexKey ?? string.Empty);
        using var hmac = new HMACSHA512(keyBytes);
        var hash = hmac.ComputeHash(Encoding.ASCII.GetBytes(message));
        return Convert.ToHexString(hash);
    }

    protected virtual byte[] PackHex(string hex)
    {
        if (hex.Length % 2 == 1)
            hex += "0";

        var data = new byte[hex.Length / 2];
        for (var i = 0; i < hex.Length; i += 2)
            data[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);

        return data;
    }

    protected virtual string CropAndSanitize(string value, int maxLength, bool xmlEncode = true)
    {
        value ??= string.Empty;
        if (value.Length > maxLength)
            value = value[..maxLength];

        return xmlEncode ? System.Security.SecurityElement.Escape(value) : value;
    }

    protected virtual string RemoveDiacritics(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var normalized = text.Normalize(NormalizationForm.FormKD);
        var sb = new StringBuilder();
        foreach (var c in normalized)
        {
            var uc = CharUnicodeInfo.GetUnicodeCategory(c);
            if (uc is not UnicodeCategory.NonSpacingMark and not UnicodeCategory.SpacingCombiningMark and not UnicodeCategory.EnclosingMark)
                sb.Append(c);
        }

        return sb.ToString();
    }
}
