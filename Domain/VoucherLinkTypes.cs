namespace TexTrack.Web.Domain;

/// <summary>
/// Canonical VoucherLink.LinkType vocabulary. Use these constants rather than
/// inline strings so a single link type is never spelled two different ways
/// across native-create and any future import path (an existing drift exists
/// between "JWO_TO_MATERIAL_OUT" and "JWO_TO_MO" - do not repeat that here).
/// </summary>
public static class VoucherLinkTypes
{
    public const string PurchaseOrderToPurchase = "PO_TO_PURCHASE";
}
