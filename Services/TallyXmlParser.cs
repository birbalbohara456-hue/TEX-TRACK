using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using TexTrack.Web.Models;

namespace TexTrack.Web.Services;

public sealed partial class TallyXmlParser
{
    private static readonly string[] SupportedVoucherTypes = ["Job Work Out Order", "Material Out", "Material In"];
    private static readonly string[] MasterTypes = ["LEDGER", "GROUP", "STOCKGROUP", "STOCKCATEGORY", "STOCKITEM", "UNIT", "GODOWN", "VOUCHERTYPE"];

    public TallyXmlDocument Parse(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) throw new InvalidOperationException("The selected XML file is empty.");
        var sanitized = InvalidControlReference().Replace(xml, string.Empty);
        XDocument document;
        try { document = XDocument.Parse(sanitized, LoadOptions.PreserveWhitespace); }
        catch (Exception exception) { throw new InvalidOperationException($"Tally XML is invalid: {exception.Message}", exception); }

        var result = new TallyXmlDocument
        {
            CompanyName = Value(document, "SVCURRENTCOMPANY"),
            FileHash = Hash(sanitized)
        };

        foreach (var message in document.Descendants().Where(x => x.Name.LocalName == "TALLYMESSAGE"))
        {
            foreach (var child in message.Elements())
            {
                var type = child.Name.LocalName;
                if (type == "VOUCHER")
                {
                    var voucher = ParseVoucher(child);
                    if (SupportedVoucherTypes.Contains(voucher.VoucherTypeName, StringComparer.OrdinalIgnoreCase))
                        result.Vouchers.Add(voucher);
                }
                else if (MasterTypes.Contains(type, StringComparer.OrdinalIgnoreCase))
                {
                    result.Masters.Add(ParseMaster(child, type));
                }
            }
        }

        var identityGuid = result.Vouchers.Select(x => x.Guid)
            .Concat(result.Masters.Select(x => x.Guid))
            .FirstOrDefault(x => x.Length >= 36) ?? string.Empty;
        result.CompanyIdentity = identityGuid.Length >= 36 ? identityGuid[..36] : Normalize(result.CompanyName);
        if (string.IsNullOrWhiteSpace(result.CompanyName)) result.CompanyName = "Unspecified Tally Company";
        return result;
    }

    private static TallyXmlMaster ParseMaster(XElement element, string type) => new()
    {
        Type = type,
        Name = Decode((string?)element.Attribute("NAME") ?? Value(element, "NAME")),
        Guid = Value(element, "GUID"),
        Parent = CleanApplicable(Value(element, "PARENT")),
        Category = CleanApplicable(Value(element, "CATEGORY")),
        BaseUnits = CleanApplicable(Value(element, "BASEUNITS")),
        OriginalName = CleanApplicable(Value(element, "ORIGINALNAME")),
        DecimalPlaces = int.TryParse(Value(element, "DECIMALPLACES"), out var places) ? places : 0,
        IsDeleted = Yes(Value(element, "ISDELETED"))
    };

    private static TallyXmlVoucher ParseVoucher(XElement element)
    {
        var voucher = new TallyXmlVoucher
        {
            Guid = Value(element, "GUID"),
            RemoteId = (string?)element.Attribute("REMOTEID") ?? string.Empty,
            Action = (string?)element.Attribute("ACTION") ?? "Create",
            VoucherTypeName = Value(element, "VOUCHERTYPENAME"),
            VoucherNumber = Value(element, "VOUCHERNUMBER"),
            ReferenceNumber = Value(element, "REFERENCE"),
            OrderReferenceNumber = CleanApplicable(Value(element, "BASICPURCHASEORDERNO")),
            DestinationGodownName = CleanApplicable(Value(element, "VOUCHERDESTINATIONGODOWN")),
            SourceGodownName = CleanApplicable(Value(element, "VOUCHERSOURCEGODOWN")),
            VoucherDate = ParseDate(Value(element, "DATE")),
            PartyLedgerName = Value(element, "PARTYLEDGERNAME"),
            Narration = Value(element, "NARRATION"),
            IsCancelled = Yes(Value(element, "ISCANCELLED")) || string.Equals((string?)element.Attribute("ACTION"), "Cancel", StringComparison.OrdinalIgnoreCase),
            IsDeleted = Yes(Value(element, "ISDELETED")) || string.Equals((string?)element.Attribute("ACTION"), "Delete", StringComparison.OrdinalIgnoreCase),
            RawXml = element.ToString(SaveOptions.DisableFormatting)
        };

        if (string.IsNullOrWhiteSpace(voucher.DestinationGodownName))
            voucher.DestinationGodownName = CleanApplicable(Value(element, "DESTINATIONGODOWN"));

        var inventoryContainers = element.Elements()
            .Where(x => x.Name.LocalName is "ALLINVENTORYENTRIES.LIST" or "INVENTORYENTRIESIN.LIST" or "INVENTORYENTRIESOUT.LIST")
            .ToList();
        var directionalContainers = inventoryContainers
            .Where(x => x.Name.LocalName is "INVENTORYENTRIESIN.LIST" or "INVENTORYENTRIESOUT.LIST")
            .ToList();
        foreach (var container in directionalContainers.Count > 0 ? directionalContainers : inventoryContainers)
        {
            var direction = container.Name.LocalName.StartsWith("INVENTORYENTRIESIN", StringComparison.Ordinal) ? "In"
                : container.Name.LocalName.StartsWith("INVENTORYENTRIESOUT", StringComparison.Ordinal) ? "Out" : "Order";
            voucher.InventoryLines.Add(ParseInventoryLine(container, direction));
        }

        voucher.SourceHash = Hash(voucher.RawXml);
        return voucher;
    }

    private static TallyXmlInventoryLine ParseInventoryLine(XElement container, string direction)
    {
        var quantity = ParseQuantity(ChildValue(container, "ACTUALQTY"));
        var batch = container.Elements().FirstOrDefault(x => x.Name.LocalName == "BATCHALLOCATIONS.LIST");
        var allocation = batch ?? container;
        var line = new TallyXmlInventoryLine
        {
            Direction = direction,
            StockItemName = ChildValue(container, "STOCKITEMNAME"),
            Quantity = quantity.Quantity,
            UqcName = quantity.Uqc,
            Rate = ParseRate(ChildValue(container, "RATE")),
            Amount = ParseDecimal(ChildValue(container, "AMOUNT")),
            GodownName = CleanApplicable(ChildValue(allocation, "GODOWNNAME")),
            DestinationGodownName = CleanApplicable(ChildValue(allocation, "DESTINATIONGODOWNNAME")),
            BatchName = CleanApplicable(ChildValue(allocation, "BATCHNAME")),
            OrderNumber = CleanApplicable(ChildValue(allocation, "ORDERNO"))
        };

        if (batch is not null)
        {
            foreach (var component in batch.Elements().Where(x => x.Name.LocalName == "VOUCHERCOMPONENTLIST.LIST"))
                line.Components.Add(ParseInventoryLine(component, "Component"));
        }

        return line;
    }

    private static string ChildValue(XContainer element, string localName) => Decode(element.Elements()
        .FirstOrDefault(x => x.Name.LocalName == localName)?.Value.Trim() ?? string.Empty);

    private static string Value(XContainer element, string localName) => Decode(element.Descendants()
        .FirstOrDefault(x => x.Name.LocalName == localName)?.Value.Trim() ?? string.Empty);

    private static (decimal Quantity, string Uqc) ParseQuantity(string value)
    {
        var match = QuantityPattern().Match(value.Trim());
        if (!match.Success) return (0, string.Empty);
        return (Math.Abs(ParseDecimal(match.Groups[1].Value)), match.Groups[2].Value.Trim());
    }

    private static decimal ParseRate(string value)
    {
        var slash = value.IndexOf('/');
        return ParseDecimal(slash >= 0 ? value[..slash] : value);
    }

    private static decimal ParseDecimal(string value)
    {
        var normalized = value.Replace(",", string.Empty).Trim();
        return decimal.TryParse(normalized, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var result) ? result : 0;
    }

    private static DateOnly ParseDate(string value) => DateOnly.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
        ? date : throw new InvalidOperationException($"Tally voucher date '{value}' is invalid.");

    private static string CleanApplicable(string value) => value.Contains("Not Applicable", StringComparison.OrdinalIgnoreCase) ? string.Empty : value.Trim();
    private static string Decode(string value) => System.Net.WebUtility.HtmlDecode(value).Replace("\u0004", string.Empty).Trim();
    private static bool Yes(string value) => string.Equals(value, "Yes", StringComparison.OrdinalIgnoreCase);
    private static string Normalize(string value) => string.Join(' ', value.Trim().ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    [GeneratedRegex(@"&#(?:4|x0*4);", RegexOptions.IgnoreCase)]
    private static partial Regex InvalidControlReference();

    [GeneratedRegex(@"^\s*([+-]?[0-9.,]+)\s+(.+?)\s*$")]
    private static partial Regex QuantityPattern();
}
