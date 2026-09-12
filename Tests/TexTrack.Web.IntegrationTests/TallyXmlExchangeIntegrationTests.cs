using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Xml.Linq;
using TexTrack.Web.Domain;
using Xunit;

namespace TexTrack.Web.IntegrationTests;

public sealed class TallyXmlExchangeIntegrationTests : IAsyncLifetime
{
    private PostgreSqlTestEnvironment environment = null!;

    public async Task InitializeAsync()
    {
        environment = await PostgreSqlTestEnvironment.CreateAsync();
        await environment.SeedAsync();
    }

    public async Task DisposeAsync() => await environment.DisposeAsync();

    [Fact]
    public async Task Tally_short_unit_name_reuses_existing_textrack_uqc_without_duplicate()
    {
        const string xml = """
            <ENVELOPE><BODY><IMPORTDATA><REQUESTDESC><STATICVARIABLES><SVCURRENTCOMPANY>Test Company</SVCURRENTCOMPANY></STATICVARIABLES></REQUESTDESC>
            <REQUESTDATA><TALLYMESSAGE><UNIT NAME="PCS"><NAME>PCS</NAME><GUID>77777777-7777-7777-7777-777777777777-unit</GUID><DECIMALPLACES>0</DECIMALPLACES></UNIT></TALLYMESSAGE></REQUESTDATA>
            </IMPORTDATA></BODY></ENVELOPE>
            """;

        var preview = await environment.TallyXmlExchangeService.PreviewAsync("unit.xml", xml);
        Assert.Equal(0, preview.NewMasterCount);

        var result = await environment.TallyXmlExchangeService.ApplyAsync(preview);
        Assert.True(result.Success, result.Message);

        await using var db = environment.CreateDbContext();
        Assert.Equal(1, await db.Uqcs.CountAsync(x => x.CompanyId == 1 && x.ShortName == "PCS"));
        Assert.Equal("Pieces", await db.Uqcs.Where(x => x.ShortName == "PCS").Select(x => x.Name).SingleAsync());
    }

    [Fact]
    public async Task Jwo_export_nests_components_under_the_finished_good_and_balances_values()
    {
        await using (var db = environment.CreateDbContext())
        {
            var type = await db.VoucherTypes.SingleAsync(x => x.Id == 1);
            type.SystemTypeCode = "JOB_WORK_OUT_ORDER";
            var voucher = await db.Vouchers.SingleAsync(x => x.Id == 1);
            voucher.ReferenceNumber = "ORDER-1";
            db.JobWorkOrderFinishedGoods.Add(new JobWorkOrderFinishedGood
            {
                Id = 10, VoucherId = 1, LineNumber = 1, StockItemId = 2, OrderedQuantity = 10,
                FinishedGoodsGodownId = 1, DestinationGodownId = 2
            });
            db.JobWorkOrderComponents.Add(new JobWorkOrderComponent
            {
                Id = 20, FinishedGoodId = 10, LineNumber = 1, StockItemId = 3, UqcId = 1,
                RequiredQuantity = 20, ComponentGodownId = 1, XmlRate = 50, XmlAmount = 1000
            });
            await db.SaveChangesAsync();
        }

        var export = await environment.TallyXmlExporter.ExportAsync("jwo", new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31));
        var document = XDocument.Parse(Encoding.UTF8.GetString(export.Content));
        var voucherElement = Assert.Single(document.Descendants().Where(x => x.Name.LocalName == "VOUCHER"));
        var finishedGood = Assert.Single(voucherElement.Elements().Where(x => x.Name.LocalName == "ALLINVENTORYENTRIES.LIST"));
        Assert.Equal("Helper Finished Good", Child(finishedGood, "STOCKITEMNAME"));
        Assert.Equal("-1000.00", Child(finishedGood, "AMOUNT"));
        var batch = Assert.Single(finishedGood.Elements().Where(x => x.Name.LocalName == "BATCHALLOCATIONS.LIST"));
        Assert.Equal("Main Godown", Child(batch, "GODOWNNAME"));
        Assert.Equal("Job Worker Godown", Child(batch, "DESTINATIONGODOWNNAME"));
        Assert.Equal("JobOrder", Child(batch, "ORDERTYPE"));
        Assert.Equal("ORDER-1", Child(batch, "ORDERNO"));
        var component = Assert.Single(batch.Elements().Where(x => x.Name.LocalName == "VOUCHERCOMPONENTLIST.LIST"));
        Assert.Equal("Helper Component", Child(component, "STOCKITEMNAME"));
        Assert.Equal("1000.00", Child(component, "AMOUNT"));
        var componentBatch = Assert.Single(component.Elements().Where(x => x.Name.LocalName == "BATCHALLOCATIONS.LIST"));
        Assert.Equal("SubOrder", Child(componentBatch, "ORDERTYPE"));
        Assert.Equal("Helper Finished Good", Child(componentBatch, "PARENTITEM"));
        Assert.Equal(0m, decimal.Parse(Child(finishedGood, "AMOUNT")) + decimal.Parse(Child(component, "AMOUNT")));
    }

    [Fact]
    public async Task Authentic_nested_jwo_import_maps_finished_good_component_and_destination_independently()
    {
        await using (var db = environment.CreateDbContext())
        {
            var type = await db.VoucherTypes.SingleAsync(x => x.Id == 1);
            type.SystemTypeCode = "JOB_WORK_OUT_ORDER";
            db.VoucherTypes.Add(new VoucherType
            {
                Id = 2, CompanyId = 1, Name = "Material Out", NameNormalized = "MATERIAL OUT",
                SystemTypeCode = "MATERIAL_OUT", Nature = "Inventory", PostingMode = "Stock",
                Abbreviation = "MO", TallyVoucherTypeName = "Material Out", IsActive = true,
                CreatedAtUtc = DateTimeOffset.UtcNow, ModifiedAtUtc = DateTimeOffset.UtcNow
            });
            db.VoucherTypes.Add(new VoucherType
            {
                Id = 3, CompanyId = 1, Name = "Material In", NameNormalized = "MATERIAL IN",
                SystemTypeCode = "MATERIAL_IN", Nature = "Inventory", PostingMode = "Stock",
                Abbreviation = "MI", TallyVoucherTypeName = "Material In", IsActive = true,
                CreatedAtUtc = DateTimeOffset.UtcNow, ModifiedAtUtc = DateTimeOffset.UtcNow
            });
            db.Vouchers.Remove(await db.Vouchers.SingleAsync(x => x.Id == 1));
            await db.SaveChangesAsync();
        }

        const string xml = """
            <ENVELOPE><BODY><IMPORTDATA><REQUESTDESC><STATICVARIABLES><SVCURRENTCOMPANY>Test Company</SVCURRENTCOMPANY></STATICVARIABLES></REQUESTDESC>
            <REQUESTDATA><TALLYMESSAGE><VOUCHER REMOTEID="nested-jwo" VCHTYPE="Job Work Out Order" ACTION="Create">
            <DATE>20260802</DATE><GUID>88888888-8888-8888-8888-888888888888-jwo</GUID><VOUCHERTYPENAME>Job Work Out Order</VOUCHERTYPENAME>
            <VOUCHERNUMBER>JWO-IMPORT</VOUCHERNUMBER><REFERENCE>ORDER-IMPORT</REFERENCE><PARTYLEDGERNAME>Imported Worker</PARTYLEDGERNAME>
            <ALLINVENTORYENTRIES.LIST><STOCKITEMNAME>Helper Finished Good</STOCKITEMNAME><COMPONENTLISTTYPE>Track Components</COMPONENTLISTTYPE>
            <ISDEEMEDPOSITIVE>Yes</ISDEEMEDPOSITIVE><ISTRACKCOMPONENT>Yes</ISTRACKCOMPONENT><RATE>100/PCS</RATE><AMOUNT>-1000</AMOUNT><ACTUALQTY>10 PCS</ACTUALQTY>
            <BATCHALLOCATIONS.LIST><GODOWNNAME>Main Godown</GODOWNNAME><DESTINATIONGODOWNNAME>Job Worker Godown</DESTINATIONGODOWNNAME><ORDERNO>ORDER-IMPORT</ORDERNO>
            <VOUCHERCOMPONENTLIST.LIST><STOCKITEMNAME>Helper Component</STOCKITEMNAME><NATUREOFCOMPONENT>Pending to Issue</NATUREOFCOMPONENT>
            <ISDEEMEDPOSITIVE>No</ISDEEMEDPOSITIVE><RATE>50/PCS</RATE><AMOUNT>1000</AMOUNT><ACTUALQTY>20 PCS</ACTUALQTY>
            <BATCHALLOCATIONS.LIST><GODOWNNAME>Main Godown</GODOWNNAME><ORDERTYPE>SubOrder</ORDERTYPE><PARENTITEM>Helper Finished Good</PARENTITEM><ORDERNO>ORDER-IMPORT</ORDERNO></BATCHALLOCATIONS.LIST>
            </VOUCHERCOMPONENTLIST.LIST></BATCHALLOCATIONS.LIST></ALLINVENTORYENTRIES.LIST></VOUCHER></TALLYMESSAGE></REQUESTDATA>
            </IMPORTDATA></BODY></ENVELOPE>
            """;

        var preview = await environment.TallyXmlExchangeService.PreviewAsync("nested-jwo.xml", xml);
        Assert.Equal("New", Assert.Single(preview.Rows).State);
        var result = await environment.TallyXmlExchangeService.ApplyAsync(preview);
        Assert.True(result.Success, result.Message);

        await using var verify = environment.CreateDbContext();
        var voucher = await verify.Vouchers.SingleAsync(x => x.VoucherNumber == "JWO-IMPORT");
        var finishedGood = await verify.JobWorkOrderFinishedGoods
            .Include(x => x.StockItem).Include(x => x.FinishedGoodsGodown).Include(x => x.DestinationGodown)
            .SingleAsync(x => x.VoucherId == voucher.Id);
        Assert.Equal("Helper Finished Good", finishedGood.StockItem.Name);
        Assert.Equal("Main Godown", finishedGood.FinishedGoodsGodown!.Name);
        Assert.Equal("Job Worker Godown", finishedGood.DestinationGodown!.Name);
        Assert.Equal(-1000m, finishedGood.XmlAmount);
        var component = await verify.JobWorkOrderComponents.Include(x => x.StockItem).Include(x => x.ComponentGodown)
            .SingleAsync(x => x.FinishedGoodId == finishedGood.Id);
        Assert.Equal("Helper Component", component.StockItem.Name);
        Assert.Equal("Main Godown", component.ComponentGodown!.Name);
        Assert.Equal(20m, component.RequiredQuantity);
        Assert.Equal(1000m, component.XmlAmount);
        Assert.Equal(3, component.ComponentVariantId);

        const string materialOutXml = """
            <ENVELOPE><BODY><IMPORTDATA><REQUESTDESC><STATICVARIABLES><SVCURRENTCOMPANY>Test Company</SVCURRENTCOMPANY></STATICVARIABLES></REQUESTDESC>
            <REQUESTDATA><TALLYMESSAGE><VOUCHER REMOTEID="nested-mo" VCHTYPE="Material Out" ACTION="Create">
            <DATE>20260803</DATE><GUID>99999999-9999-9999-9999-999999999999-mo</GUID><VOUCHERTYPENAME>Material Out</VOUCHERTYPENAME>
            <VOUCHERNUMBER>MO-IMPORT</VOUCHERNUMBER><PARTYLEDGERNAME>Imported Worker</PARTYLEDGERNAME>
            <VOUCHERDESTINATIONGODOWN>Job Worker Godown</VOUCHERDESTINATIONGODOWN>
            <INVENTORYENTRIESIN.LIST><STOCKITEMNAME>Helper Component</STOCKITEMNAME><RATE>50/PCS</RATE><AMOUNT>-1000</AMOUNT><ACTUALQTY>20 PCS</ACTUALQTY>
            <BATCHALLOCATIONS.LIST><GODOWNNAME>Job Worker Godown</GODOWNNAME></BATCHALLOCATIONS.LIST></INVENTORYENTRIESIN.LIST>
            <INVENTORYENTRIESOUT.LIST><STOCKITEMNAME>Helper Component</STOCKITEMNAME><RATE>50/PCS</RATE><AMOUNT>1000</AMOUNT><ACTUALQTY>20 PCS</ACTUALQTY>
            <BATCHALLOCATIONS.LIST><GODOWNNAME>Main Godown</GODOWNNAME><ORDERNO>ORDER-IMPORT</ORDERNO></BATCHALLOCATIONS.LIST></INVENTORYENTRIESOUT.LIST>
            </VOUCHER></TALLYMESSAGE></REQUESTDATA></IMPORTDATA></BODY></ENVELOPE>
            """;
        var materialOutPreview = await environment.TallyXmlExchangeService.PreviewAsync("material-out.xml", materialOutXml);
        Assert.Equal("New", Assert.Single(materialOutPreview.Rows).State);
        var materialOutResult = await environment.TallyXmlExchangeService.ApplyAsync(materialOutPreview);
        Assert.True(materialOutResult.Success, materialOutResult.Message);
        var materialOutLine = await verify.MaterialOutLines.Include(x => x.SourceGodown).Include(x => x.DestinationGodown)
            .SingleAsync(x => x.JwoComponentId == component.Id);
        Assert.Equal("Main Godown", materialOutLine.SourceGodown.Name);
        Assert.Equal("Job Worker Godown", materialOutLine.DestinationGodown.Name);
        Assert.Equal(20m, materialOutLine.IssuedQuantity);
        Assert.All(
            await verify.StockMovements.Where(x => x.VoucherId == materialOutLine.VoucherId).ToListAsync(),
            movement => Assert.Equal(component.ComponentVariantId, movement.StockItemVariantId));
        var materialOutDetail = await verify.MaterialOutDetails.SingleAsync(x => x.VoucherId == materialOutLine.VoucherId);
        Assert.Equal("JWO-IMPORT", materialOutDetail.DisplayedOrderNumber);

        var materialExport = await environment.TallyXmlExporter.ExportAsync("material", new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31));
        var exportedDocument = XDocument.Parse(Encoding.UTF8.GetString(materialExport.Content));
        var exportedMaterialOut = Assert.Single(exportedDocument.Descendants("VOUCHER")
            .Where(x => Child(x, "VOUCHERTYPENAME") == "Material Out"));
        Assert.Equal("Main Godown", Child(exportedMaterialOut, "VOUCHERSOURCEGODOWN"));
        Assert.Equal("Job Worker Godown", Child(exportedMaterialOut, "VOUCHERDESTINATIONGODOWN"));
        var invoiceOrder = Assert.Single(exportedMaterialOut.Elements("INVOICEORDERLIST.LIST"));
        Assert.Equal("BOM Out Order", Child(invoiceOrder, "ORDERTYPE"));
        Assert.Equal("ORDER-IMPORT", Child(invoiceOrder, "BASICPURCHASEORDERNO"));
        var exportedOutLine = Assert.Single(exportedMaterialOut.Elements("INVENTORYENTRIESOUT.LIST"));
        var exportedOutBatch = Assert.Single(exportedOutLine.Elements("BATCHALLOCATIONS.LIST"));
        Assert.Equal("ORDER-IMPORT", Child(exportedOutBatch, "ORDERNO"));
        Assert.Equal("SubOrder", Child(exportedOutBatch, "ORDERTYPE"));
        Assert.Equal("Helper Finished Good", Child(exportedOutBatch, "PARENTITEM"));

        const string materialInXml = """
            <ENVELOPE><BODY><IMPORTDATA><REQUESTDESC><STATICVARIABLES><SVCURRENTCOMPANY>Test Company</SVCURRENTCOMPANY></STATICVARIABLES></REQUESTDESC>
            <REQUESTDATA><TALLYMESSAGE><VOUCHER REMOTEID="nested-mi" VCHTYPE="Material In" ACTION="Create">
            <DATE>20260804</DATE><GUID>aaaaaaaa-9999-9999-9999-999999999999-mi</GUID><VOUCHERTYPENAME>Material In</VOUCHERTYPENAME>
            <VOUCHERNUMBER>MI-IMPORT</VOUCHERNUMBER><PARTYLEDGERNAME>Imported Worker</PARTYLEDGERNAME>
            <VOUCHERSOURCEGODOWN>Job Worker Godown</VOUCHERSOURCEGODOWN><VOUCHERDESTINATIONGODOWN>Main Godown</VOUCHERDESTINATIONGODOWN>
            <INVOICEORDERLIST.LIST><ORDERTYPE>BOM In Order</ORDERTYPE><BASICPURCHASEORDERNO>JWO-IMPORT</BASICPURCHASEORDERNO></INVOICEORDERLIST.LIST>
            <INVENTORYENTRIESIN.LIST><STOCKITEMNAME>Helper Finished Good</STOCKITEMNAME><RATE>150/PCS</RATE><AMOUNT>-600</AMOUNT><ACTUALQTY>4 PCS</ACTUALQTY>
            <BATCHALLOCATIONS.LIST><GODOWNNAME>Main Godown</GODOWNNAME><ORDERNO>JWO-IMPORT</ORDERNO></BATCHALLOCATIONS.LIST></INVENTORYENTRIESIN.LIST>
            <INVENTORYENTRIESOUT.LIST><STOCKITEMNAME>Helper Component</STOCKITEMNAME><RATE>50/PCS</RATE><AMOUNT>400</AMOUNT><ACTUALQTY>8 PCS</ACTUALQTY>
            <BATCHALLOCATIONS.LIST><GODOWNNAME>Job Worker Godown</GODOWNNAME></BATCHALLOCATIONS.LIST></INVENTORYENTRIESOUT.LIST>
            </VOUCHER></TALLYMESSAGE></REQUESTDATA></IMPORTDATA></BODY></ENVELOPE>
            """;
        var materialInPreview = await environment.TallyXmlExchangeService.PreviewAsync("material-in.xml", materialInXml);
        Assert.Equal("New", Assert.Single(materialInPreview.Rows).State);
        var materialInResult = await environment.TallyXmlExchangeService.ApplyAsync(materialInPreview);
        Assert.True(materialInResult.Success, materialInResult.Message);

        var materialInDetail = await verify.MaterialInDetails.Include(x => x.JwoVoucher)
            .SingleAsync(x => x.Voucher.VoucherNumber == "MI-IMPORT");
        Assert.Equal("JWO-IMPORT", materialInDetail.DisplayedOrderNumber);
        Assert.Equal(200m, materialInDetail.TotalProcessCharge);
        var importedConsumptionMovement = await verify.StockMovements.SingleAsync(x =>
            x.VoucherId == materialInDetail.VoucherId && x.MovementKind == "MaterialInConsumption");
        Assert.Equal(component.ComponentVariantId, importedConsumptionMovement.StockItemVariantId);

        materialExport = await environment.TallyXmlExporter.ExportAsync("material", new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31));
        exportedDocument = XDocument.Parse(Encoding.UTF8.GetString(materialExport.Content));
        var exportedMaterialIn = Assert.Single(exportedDocument.Descendants("VOUCHER")
            .Where(x => Child(x, "VOUCHERTYPENAME") == "Material In"));
        Assert.Equal("Job Worker Godown", Child(exportedMaterialIn, "VOUCHERSOURCEGODOWN"));
        Assert.Equal("Main Godown", Child(exportedMaterialIn, "VOUCHERDESTINATIONGODOWN"));
        var materialInOrder = Assert.Single(exportedMaterialIn.Elements("INVOICEORDERLIST.LIST"));
        Assert.Equal("BOM In Order", Child(materialInOrder, "ORDERTYPE"));
        Assert.Equal("ORDER-IMPORT", Child(materialInOrder, "BASICPURCHASEORDERNO"));
        var exportedFinishedLine = Assert.Single(exportedMaterialIn.Elements("INVENTORYENTRIESIN.LIST"));
        Assert.Equal("ORDER-IMPORT", Child(Assert.Single(exportedFinishedLine.Elements("BATCHALLOCATIONS.LIST")), "ORDERNO"));
        Assert.Empty(exportedMaterialIn.Elements("INVENTORYENTRIESOUT.LIST"));
        Assert.Contains("Consumed Helper Component: 8 PCS @ 50", Child(exportedMaterialIn, "NARRATION"));
    }

    [Fact]
    public async Task Existing_stock_item_with_different_uqc_is_rejected_instead_of_silently_remapped()
    {
        await using (var db = environment.CreateDbContext())
        {
            (await db.VoucherTypes.SingleAsync(x => x.Id == 1)).SystemTypeCode = "JOB_WORK_OUT_ORDER";
            db.Vouchers.Remove(await db.Vouchers.SingleAsync(x => x.Id == 1));
            await db.SaveChangesAsync();
        }

        const string xml = """
            <ENVELOPE><BODY><IMPORTDATA><REQUESTDESC><STATICVARIABLES><SVCURRENTCOMPANY>Test Company</SVCURRENTCOMPANY></STATICVARIABLES></REQUESTDESC>
            <REQUESTDATA><TALLYMESSAGE><VOUCHER REMOTEID="uqc-conflict" VCHTYPE="Job Work Out Order" ACTION="Create">
            <DATE>20260802</DATE><GUID>aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa-jwo</GUID><VOUCHERTYPENAME>Job Work Out Order</VOUCHERTYPENAME>
            <VOUCHERNUMBER>JWO-UQC-CONFLICT</VOUCHERNUMBER><PARTYLEDGERNAME>Imported Worker</PARTYLEDGERNAME>
            <ALLINVENTORYENTRIES.LIST><STOCKITEMNAME>Helper Finished Good</STOCKITEMNAME><RATE>100/MTS</RATE><AMOUNT>-1000</AMOUNT><ACTUALQTY>10 MTS</ACTUALQTY>
            <BATCHALLOCATIONS.LIST><GODOWNNAME>Main Godown</GODOWNNAME><ORDERNO>ORDER-UQC</ORDERNO></BATCHALLOCATIONS.LIST>
            </ALLINVENTORYENTRIES.LIST></VOUCHER></TALLYMESSAGE></REQUESTDATA></IMPORTDATA></BODY></ENVELOPE>
            """;

        var preview = await environment.TallyXmlExchangeService.PreviewAsync("uqc-conflict.xml", xml);
        var result = await environment.TallyXmlExchangeService.ApplyAsync(preview);
        Assert.False(result.Success);
        Assert.Contains("uses UQC 'PCS' in TexTrack but the XML uses 'MTS'", result.Message);
        await using var verify = environment.CreateDbContext();
        Assert.False(await verify.Vouchers.AnyAsync(x => x.VoucherNumber == "JWO-UQC-CONFLICT"));
    }

    [Fact]
    public async Task Tally_material_out_uses_the_same_negative_stock_policy_as_native_entry()
    {
        await using (var db = environment.CreateDbContext())
        {
            (await db.VoucherTypes.SingleAsync(x => x.Id == 1)).SystemTypeCode = "JOB_WORK_OUT_ORDER";
            db.VoucherTypes.Add(new VoucherType
            {
                Id = 2, CompanyId = 1, Name = "Material Out", NameNormalized = "MATERIAL OUT",
                SystemTypeCode = "MATERIAL_OUT", Nature = "Inventory", PostingMode = "Stock",
                Abbreviation = "MO", TallyVoucherTypeName = "Material Out", IsActive = true,
                CreatedAtUtc = DateTimeOffset.UtcNow, ModifiedAtUtc = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        const string jwoXml = """
            <ENVELOPE><BODY><IMPORTDATA><REQUESTDESC><STATICVARIABLES><SVCURRENTCOMPANY>Test Company</SVCURRENTCOMPANY></STATICVARIABLES></REQUESTDESC>
            <REQUESTDATA><TALLYMESSAGE><VOUCHER REMOTEID="negative-jwo" VCHTYPE="Job Work Out Order" ACTION="Create">
            <DATE>20260802</DATE><GUID>bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb-jwo</GUID><VOUCHERTYPENAME>Job Work Out Order</VOUCHERTYPENAME>
            <VOUCHERNUMBER>JWO-NEGATIVE</VOUCHERNUMBER><REFERENCE>ORDER-NEGATIVE</REFERENCE><PARTYLEDGERNAME>Imported Worker</PARTYLEDGERNAME>
            <ALLINVENTORYENTRIES.LIST><STOCKITEMNAME>Helper Finished Good</STOCKITEMNAME><RATE>100/PCS</RATE><AMOUNT>-100</AMOUNT><ACTUALQTY>1 PCS</ACTUALQTY>
            <BATCHALLOCATIONS.LIST><GODOWNNAME>Main Godown</GODOWNNAME><DESTINATIONGODOWNNAME>Job Worker Godown</DESTINATIONGODOWNNAME><ORDERNO>ORDER-NEGATIVE</ORDERNO>
            <VOUCHERCOMPONENTLIST.LIST><STOCKITEMNAME>Helper Component</STOCKITEMNAME><RATE>50/PCS</RATE><AMOUNT>50</AMOUNT><ACTUALQTY>1 PCS</ACTUALQTY>
            <BATCHALLOCATIONS.LIST><GODOWNNAME>Main Godown</GODOWNNAME><ORDERTYPE>SubOrder</ORDERTYPE><PARENTITEM>Helper Finished Good</PARENTITEM><ORDERNO>ORDER-NEGATIVE</ORDERNO></BATCHALLOCATIONS.LIST>
            </VOUCHERCOMPONENTLIST.LIST></BATCHALLOCATIONS.LIST></ALLINVENTORYENTRIES.LIST></VOUCHER></TALLYMESSAGE></REQUESTDATA>
            </IMPORTDATA></BODY></ENVELOPE>
            """;
        var jwoPreview = await environment.TallyXmlExchangeService.PreviewAsync("negative-jwo.xml", jwoXml);
        Assert.True((await environment.TallyXmlExchangeService.ApplyAsync(jwoPreview)).Success);

        await using (var db = environment.CreateDbContext())
        {
            await db.Companies.Where(x => x.Id == 1)
                .ExecuteUpdateAsync(x => x.SetProperty(c => c.AllowNegativeStock, false));
        }

        const string materialOutXml = """
            <ENVELOPE><BODY><IMPORTDATA><REQUESTDESC><STATICVARIABLES><SVCURRENTCOMPANY>Test Company</SVCURRENTCOMPANY></STATICVARIABLES></REQUESTDESC>
            <REQUESTDATA><TALLYMESSAGE><VOUCHER REMOTEID="negative-mo" VCHTYPE="Material Out" ACTION="Create">
            <DATE>20260803</DATE><GUID>cccccccc-cccc-cccc-cccc-cccccccccccc-mo</GUID><VOUCHERTYPENAME>Material Out</VOUCHERTYPENAME>
            <VOUCHERNUMBER>MO-NEGATIVE</VOUCHERNUMBER><PARTYLEDGERNAME>Imported Worker</PARTYLEDGERNAME>
            <VOUCHERDESTINATIONGODOWN>Job Worker Godown</VOUCHERDESTINATIONGODOWN>
            <INVENTORYENTRIESIN.LIST><STOCKITEMNAME>Helper Component</STOCKITEMNAME><RATE>50/PCS</RATE><AMOUNT>-50</AMOUNT><ACTUALQTY>1 PCS</ACTUALQTY>
            <BATCHALLOCATIONS.LIST><GODOWNNAME>Job Worker Godown</GODOWNNAME></BATCHALLOCATIONS.LIST></INVENTORYENTRIESIN.LIST>
            <INVENTORYENTRIESOUT.LIST><STOCKITEMNAME>Helper Component</STOCKITEMNAME><RATE>50/PCS</RATE><AMOUNT>50</AMOUNT><ACTUALQTY>1 PCS</ACTUALQTY>
            <BATCHALLOCATIONS.LIST><GODOWNNAME>Main Godown</GODOWNNAME><ORDERNO>ORDER-NEGATIVE</ORDERNO></BATCHALLOCATIONS.LIST></INVENTORYENTRIESOUT.LIST>
            </VOUCHER></TALLYMESSAGE></REQUESTDATA></IMPORTDATA></BODY></ENVELOPE>
            """;

        var preview = await environment.TallyXmlExchangeService.PreviewAsync("negative-mo.xml", materialOutXml);
        var result = await environment.TallyXmlExchangeService.ApplyAsync(preview);

        Assert.False(result.Success);
        Assert.Contains("Negative stock is blocked", result.Message, StringComparison.Ordinal);
        await using var verify = environment.CreateDbContext();
        Assert.False(await verify.Vouchers.AnyAsync(x => x.VoucherNumber == "MO-NEGATIVE"));
        Assert.False(await verify.StockMovements.AnyAsync(x => x.Voucher.VoucherNumber == "MO-NEGATIVE"));
    }

    private static string Child(XElement element, string name) =>
        element.Elements().Single(x => x.Name.LocalName == name).Value;
}
