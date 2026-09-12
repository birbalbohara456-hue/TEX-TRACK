using TexTrack.Web.Services;
using Xunit;

namespace TexTrack.Web.IntegrationTests;

public sealed class TallyXmlParserTests
{
    private readonly TallyXmlParser parser = new();

    [Fact]
    public void Jwo_keeps_independent_voucher_and_order_numbers_and_sanitizes_tally_control_reference()
    {
        var document = parser.Parse(Envelope("""
            <VOUCHER REMOTEID="abc" VCHTYPE="Job Work Out Order" ACTION="Create"><DATE>20260802</DATE>
            <GUID>11111111-1111-1111-1111-111111111111-extra</GUID><VOUCHERTYPENAME>Job Work Out Order</VOUCHERTYPENAME>
            <VOUCHERNUMBER>1175</VOUCHERNUMBER><REFERENCE>ORDER-99</REFERENCE><PARTYLEDGERNAME>Jobber &#4; Alpha</PARTYLEDGERNAME>
            <ALLINVENTORYENTRIES.LIST><STOCKITEMNAME>SHIRT</STOCKITEMNAME><ACTUALQTY>10 PCS</ACTUALQTY><RATE>200/PCS</RATE><AMOUNT>-2000</AMOUNT><GODOWNNAME>MAIN</GODOWNNAME><ORDERNO>ORDER-99</ORDERNO></ALLINVENTORYENTRIES.LIST>
            <ALLINVENTORYENTRIES.LIST><STOCKITEMNAME>FAB</STOCKITEMNAME><ACTUALQTY>20 MTS</ACTUALQTY><RATE>50/MTS</RATE><AMOUNT>1000</AMOUNT><GODOWNNAME>JOBBER</GODOWNNAME><ORDERNO>ORDER-99</ORDERNO></ALLINVENTORYENTRIES.LIST></VOUCHER>
            """));
        var voucher = Assert.Single(document.Vouchers);
        Assert.Equal("1175", voucher.VoucherNumber);
        Assert.Equal("ORDER-99", voucher.PrimaryOrderNumber);
        Assert.Equal(2, voucher.InventoryLines.Count);
    }

    [Fact]
    public void Material_vouchers_preserve_authentic_in_and_out_directions()
    {
        var document = parser.Parse(Envelope("""
            <VOUCHER REMOTEID="mo" VCHTYPE="Material Out" ACTION="Create"><DATE>20260802</DATE><GUID>22222222-2222-2222-2222-222222222222-mo</GUID><VOUCHERTYPENAME>Material Out</VOUCHERTYPENAME><VOUCHERNUMBER>MO-1</VOUCHERNUMBER><PARTYLEDGERNAME>Worker</PARTYLEDGERNAME>
            <INVENTORYENTRIESIN.LIST><STOCKITEMNAME>FAB</STOCKITEMNAME><ACTUALQTY>5 MTS</ACTUALQTY><AMOUNT>-250</AMOUNT><GODOWNNAME>WORKER</GODOWNNAME></INVENTORYENTRIESIN.LIST>
            <INVENTORYENTRIESOUT.LIST><STOCKITEMNAME>FAB</STOCKITEMNAME><ACTUALQTY>5 MTS</ACTUALQTY><AMOUNT>250</AMOUNT><GODOWNNAME>MAIN</GODOWNNAME><ORDERNO>JWO-1</ORDERNO></INVENTORYENTRIESOUT.LIST></VOUCHER>
            """));
        Assert.Equal(new[] { "In", "Out" }, Assert.Single(document.Vouchers).InventoryLines.Select(x => x.Direction));
    }

    [Fact]
    public void Material_out_reads_voucher_destination_and_invoice_order_fallback()
    {
        var document = parser.Parse(Envelope("""
            <VOUCHER REMOTEID="mo-authentic" VCHTYPE="Material Out" ACTION="Create"><DATE>20260802</DATE>
            <GUID>22222222-2222-2222-2222-222222222222-authentic</GUID><VOUCHERTYPENAME>Material Out</VOUCHERTYPENAME>
            <VOUCHERNUMBER>MO-2</VOUCHERNUMBER><PARTYLEDGERNAME>Worker</PARTYLEDGERNAME>
            <VOUCHERDESTINATIONGODOWN>STICH</VOUCHERDESTINATIONGODOWN>
            <INVOICEORDERLIST.LIST><BASICPURCHASEORDERNO>ORDER-2</BASICPURCHASEORDERNO></INVOICEORDERLIST.LIST>
            <INVENTORYENTRIESIN.LIST><STOCKITEMNAME>FAB</STOCKITEMNAME><ACTUALQTY>5 MTS</ACTUALQTY><GODOWNNAME>STICH</GODOWNNAME></INVENTORYENTRIESIN.LIST>
            <INVENTORYENTRIESOUT.LIST><STOCKITEMNAME>FAB</STOCKITEMNAME><ACTUALQTY>5 MTS</ACTUALQTY><GODOWNNAME>MAIN</GODOWNNAME></INVENTORYENTRIESOUT.LIST>
            </VOUCHER>
            """));

        var voucher = Assert.Single(document.Vouchers);
        Assert.Equal("STICH", voucher.DestinationGodownName);
        Assert.Equal("ORDER-2", voucher.PrimaryOrderNumber);
    }

    [Fact]
    public void Live_collection_aliases_do_not_duplicate_material_movement_lines()
    {
        var document = parser.Parse(Envelope("""
            <VOUCHER REMOTEID="live-mo" VCHTYPE="Material Out" ACTION="Create"><DATE>20260802</DATE>
            <GUID>22222222-2222-2222-2222-222222222222-live</GUID><VOUCHERTYPENAME>Material Out</VOUCHERTYPENAME><VOUCHERNUMBER>MO-LIVE</VOUCHERNUMBER>
            <ALLINVENTORYENTRIES.LIST><STOCKITEMNAME>FAB</STOCKITEMNAME><ACTUALQTY>5 MTS</ACTUALQTY></ALLINVENTORYENTRIES.LIST>
            <ALLINVENTORYENTRIES.LIST><STOCKITEMNAME>FAB</STOCKITEMNAME><ACTUALQTY>5 MTS</ACTUALQTY></ALLINVENTORYENTRIES.LIST>
            <INVENTORYENTRIESIN.LIST><STOCKITEMNAME>FAB</STOCKITEMNAME><ACTUALQTY>5 MTS</ACTUALQTY></INVENTORYENTRIESIN.LIST>
            <INVENTORYENTRIESOUT.LIST><STOCKITEMNAME>FAB</STOCKITEMNAME><ACTUALQTY>5 MTS</ACTUALQTY><ORDERNO>JWO-1</ORDERNO></INVENTORYENTRIESOUT.LIST>
            </VOUCHER>
            """));

        var voucher = Assert.Single(document.Vouchers);
        Assert.Equal(2, voucher.InventoryLines.Count);
        Assert.Equal(new[] { "In", "Out" }, voucher.InventoryLines.Select(x => x.Direction));
    }

    [Fact]
    public void Jwo_preserves_nested_components_and_separate_destination_godown()
    {
        var document = parser.Parse(Envelope("""
            <VOUCHER REMOTEID="nested" VCHTYPE="Job Work Out Order" ACTION="Create"><DATE>20260802</DATE>
            <GUID>33333333-3333-3333-3333-333333333333-jwo</GUID><VOUCHERTYPENAME>Job Work Out Order</VOUCHERTYPENAME>
            <VOUCHERNUMBER>JWO-2</VOUCHERNUMBER><PARTYLEDGERNAME>Worker</PARTYLEDGERNAME>
            <ALLINVENTORYENTRIES.LIST><STOCKITEMNAME>SHIRT</STOCKITEMNAME><ACTUALQTY>10 PCS</ACTUALQTY><RATE>100/PCS</RATE><AMOUNT>-1000</AMOUNT>
              <BATCHALLOCATIONS.LIST><GODOWNNAME>MAIN</GODOWNNAME><DESTINATIONGODOWNNAME>WORKER</DESTINATIONGODOWNNAME><ORDERNO>ORDER-2</ORDERNO>
                <VOUCHERCOMPONENTLIST.LIST><STOCKITEMNAME>FAB</STOCKITEMNAME><ACTUALQTY>20 MTS</ACTUALQTY><RATE>50/MTS</RATE><AMOUNT>1000</AMOUNT>
                  <BATCHALLOCATIONS.LIST><GODOWNNAME>RAW STORE</GODOWNNAME><ORDERNO>ORDER-2</ORDERNO></BATCHALLOCATIONS.LIST>
                </VOUCHERCOMPONENTLIST.LIST>
              </BATCHALLOCATIONS.LIST>
            </ALLINVENTORYENTRIES.LIST></VOUCHER>
            """));

        var voucher = Assert.Single(document.Vouchers);
        var finishedGood = Assert.Single(voucher.InventoryLines);
        Assert.Equal("MAIN", finishedGood.GodownName);
        Assert.Equal("WORKER", finishedGood.DestinationGodownName);
        Assert.Equal("ORDER-2", voucher.PrimaryOrderNumber);
        var component = Assert.Single(finishedGood.Components);
        Assert.Equal("FAB", component.StockItemName);
        Assert.Equal("RAW STORE", component.GodownName);
        Assert.Equal(20m, component.Quantity);
        Assert.Equal(2, voucher.AllInventoryLines.Count());
    }

    [Fact]
    public void Master_only_and_cancelled_tally_xml_are_recognized()
    {
        var document = parser.Parse(Envelope("""
            <UNIT NAME="PCS"><NAME>PCS</NAME><GUID>44444444-4444-4444-4444-444444444444-u</GUID><DECIMALPLACES>0</DECIMALPLACES></UNIT>
            <STOCKITEM NAME="SHIRT"><NAME>SHIRT</NAME><GUID>44444444-4444-4444-4444-444444444444-i</GUID><PARENT>Finished Goods</PARENT><CATEGORY>Garments</CATEGORY><BASEUNITS>PCS</BASEUNITS></STOCKITEM>
            <VOUCHER REMOTEID="cancel" VCHTYPE="Job Work Out Order" ACTION="Cancel"><DATE>20260802</DATE><GUID>44444444-4444-4444-4444-444444444444-v</GUID><VOUCHERTYPENAME>Job Work Out Order</VOUCHERTYPENAME><VOUCHERNUMBER>JWO-1</VOUCHERNUMBER><ISCANCELLED>Yes</ISCANCELLED><ISDELETED>No</ISDELETED></VOUCHER>
            """));
        Assert.Equal(2, document.Masters.Count);
        Assert.True(Assert.Single(document.Vouchers).IsCancelled);
    }

    private static string Envelope(string content) => $"<ENVELOPE><BODY><IMPORTDATA><REQUESTDESC><STATICVARIABLES><SVCURRENTCOMPANY>Demo Company</SVCURRENTCOMPANY></STATICVARIABLES></REQUESTDESC><REQUESTDATA><TALLYMESSAGE>{content}</TALLYMESSAGE></REQUESTDATA></IMPORTDATA></BODY></ENVELOPE>";
}
