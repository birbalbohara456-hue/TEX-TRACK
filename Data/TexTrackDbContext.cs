using Microsoft.EntityFrameworkCore;
using TexTrack.Web.Domain;

namespace TexTrack.Web.Data;

public sealed class TexTrackDbContext(DbContextOptions<TexTrackDbContext> options) : DbContext(options)
{
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<FinancialYear> FinancialYears => Set<FinancialYear>();
    public DbSet<ApplicationUser> ApplicationUsers => Set<ApplicationUser>();
    public DbSet<SecurityRole> SecurityRoles => Set<SecurityRole>();
    public DbSet<ApplicationUserRole> ApplicationUserRoles => Set<ApplicationUserRole>();
    public DbSet<ApplicationUserCompany> ApplicationUserCompanies => Set<ApplicationUserCompany>();
    public DbSet<LedgerGroup> LedgerGroups => Set<LedgerGroup>();
    public DbSet<Ledger> Ledgers => Set<Ledger>();
    public DbSet<Uqc> Uqcs => Set<Uqc>();
    public DbSet<StockGroup> StockGroups => Set<StockGroup>();
    public DbSet<StockCategory> StockCategories => Set<StockCategory>();
    public DbSet<Godown> Godowns => Set<Godown>();
    public DbSet<Colour> Colours => Set<Colour>();
    public DbSet<SizeMaster> Sizes => Set<SizeMaster>();
    public DbSet<ProcessMaster> Processes => Set<ProcessMaster>();
    public DbSet<TaxClassification> TaxClassifications => Set<TaxClassification>();
    public DbSet<StockItem> StockItems => Set<StockItem>();
    public DbSet<StockItemColour> StockItemColours => Set<StockItemColour>();
    public DbSet<StockItemSize> StockItemSizes => Set<StockItemSize>();
    public DbSet<StockItemVariant> StockItemVariants => Set<StockItemVariant>();
    public DbSet<StockItemFieldDefinition> StockItemFieldDefinitions => Set<StockItemFieldDefinition>();
    public DbSet<StockItemFieldValue> StockItemFieldValues => Set<StockItemFieldValue>();
    public DbSet<StockItemPhoto> StockItemPhotos => Set<StockItemPhoto>();
    public DbSet<StockItemDesignPhoto> StockItemDesignPhotos => Set<StockItemDesignPhoto>();
    public DbSet<VoucherType> VoucherTypes => Set<VoucherType>();
    public DbSet<Voucher> Vouchers => Set<Voucher>();
    public DbSet<JobWorkOrderFinishedGood> JobWorkOrderFinishedGoods => Set<JobWorkOrderFinishedGood>();
    public DbSet<JobWorkOrderSizeAllocation> JobWorkOrderSizeAllocations => Set<JobWorkOrderSizeAllocation>();
    public DbSet<MasterJobOrderFinishedGood> MasterJobOrderFinishedGoods => Set<MasterJobOrderFinishedGood>();
    public DbSet<MasterJobOrderAllocation> MasterJobOrderAllocations => Set<MasterJobOrderAllocation>();
    public DbSet<JobWorkOrderComponent> JobWorkOrderComponents => Set<JobWorkOrderComponent>();
    public DbSet<BillOfMaterial> BillOfMaterials => Set<BillOfMaterial>();
    public DbSet<BillOfMaterialLine> BillOfMaterialLines => Set<BillOfMaterialLine>();
    public DbSet<BillOfMaterialRevision> BillOfMaterialRevisions => Set<BillOfMaterialRevision>();
    public DbSet<BillOfMaterialRevisionLine> BillOfMaterialRevisionLines => Set<BillOfMaterialRevisionLine>();
    public DbSet<JobWorkOrderBomStage> JobWorkOrderBomStages => Set<JobWorkOrderBomStage>();
    public DbSet<JobWorkOrderRevision> JobWorkOrderRevisions => Set<JobWorkOrderRevision>();
    public DbSet<JobWorkOrderStageAssignment> JobWorkOrderStageAssignments => Set<JobWorkOrderStageAssignment>();

    // BUILD 2.10
    public DbSet<JobWorkOrderProcess> JobWorkOrderProcesses => Set<JobWorkOrderProcess>();

    public DbSet<MaterialOutLine> MaterialOutLines => Set<MaterialOutLine>();
    public DbSet<MaterialOutDetail> MaterialOutDetails => Set<MaterialOutDetail>();
    public DbSet<MaterialInDetail> MaterialInDetails => Set<MaterialInDetail>();
    public DbSet<MaterialInFinishedGood> MaterialInFinishedGoods => Set<MaterialInFinishedGood>();
    public DbSet<MaterialInFinishedGoodAllocation> MaterialInFinishedGoodAllocations => Set<MaterialInFinishedGoodAllocation>();
    public DbSet<MaterialInConsumption> MaterialInConsumptions => Set<MaterialInConsumption>();
    public DbSet<MaterialInMaterialOutAllocation> MaterialInMaterialOutAllocations => Set<MaterialInMaterialOutAllocation>();
    public DbSet<InventoryInwardLine> InventoryInwardLines => Set<InventoryInwardLine>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<InventoryPostingSequence> InventoryPostingSequences => Set<InventoryPostingSequence>();
    public DbSet<InventoryPosting> InventoryPostings => Set<InventoryPosting>();
    public DbSet<InventoryCostLayer> InventoryCostLayers => Set<InventoryCostLayer>();
    public DbSet<InventoryCostAllocation> InventoryCostAllocations => Set<InventoryCostAllocation>();
    public DbSet<InventoryValuationPosition> InventoryValuationPositions => Set<InventoryValuationPosition>();
    public DbSet<InventoryValuationSetting> InventoryValuationSettings => Set<InventoryValuationSetting>();
    public DbSet<InventoryValuationRun> InventoryValuationRuns => Set<InventoryValuationRun>();
    public DbSet<PurchaseOrderLine> PurchaseOrderLines => Set<PurchaseOrderLine>();
    public DbSet<PurchaseReturnLine> PurchaseReturnLines => Set<PurchaseReturnLine>();
    public DbSet<VoucherLink> VoucherLinks => Set<VoucherLink>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<VoucherAuditRevision> VoucherAuditRevisions => Set<VoucherAuditRevision>();
    public DbSet<TallyCompanyLink> TallyCompanyLinks => Set<TallyCompanyLink>();
    public DbSet<TallyExchangeBatch> TallyExchangeBatches => Set<TallyExchangeBatch>();
    public DbSet<TallySyncRecord> TallySyncRecords => Set<TallySyncRecord>();
    public DbSet<TallyImportException> TallyImportExceptions => Set<TallyImportException>();
    public DbSet<TallyVariantAllocationTask> TallyVariantAllocationTasks => Set<TallyVariantAllocationTask>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureCompany(modelBuilder.Entity<Company>());
        ConfigureFinancialYear(modelBuilder.Entity<FinancialYear>());
        ConfigureIdentity(modelBuilder);
        ConfigureNamedMaster(modelBuilder.Entity<LedgerGroup>(), "ledger_groups");
        ConfigureNamedMaster(modelBuilder.Entity<Ledger>(), "ledgers");
        ConfigureNamedMaster(modelBuilder.Entity<Uqc>(), "uqcs");
        ConfigureNamedMaster(modelBuilder.Entity<StockGroup>(), "stock_groups");
        ConfigureNamedMaster(modelBuilder.Entity<StockCategory>(), "stock_categories");
        ConfigureNamedMaster(modelBuilder.Entity<Godown>(), "godowns");
        ConfigureNamedMaster(modelBuilder.Entity<Colour>(), "colours");
        ConfigureNamedMaster(modelBuilder.Entity<SizeMaster>(), "sizes");
        ConfigureNamedMaster(modelBuilder.Entity<ProcessMaster>(), "processes");
        ConfigureNamedMaster(modelBuilder.Entity<TaxClassification>(), "tax_classifications");
        ConfigureNamedMaster(modelBuilder.Entity<StockItem>(), "stock_items");
        ConfigureNamedMaster(modelBuilder.Entity<VoucherType>(), "voucher_types");

        ConfigureTallyExchange(modelBuilder);
        ConfigureBom(modelBuilder);
        ConfigureInventoryValuation(modelBuilder);

        modelBuilder.Entity<LedgerGroup>(entity =>
        {
            entity.Property(x => x.RootClassification).HasMaxLength(80).HasColumnName("root_classification");
            entity.Property(x => x.ParentId).HasColumnName("parent_id");
            entity.HasOne(x => x.Parent)
                .WithMany(x => x.Children)
                .HasForeignKey(x => x.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Ledger>(entity =>
        {
            entity.Property(x => x.LedgerGroupId).HasColumnName("ledger_group_id");
            entity.Property(x => x.MailingName).HasMaxLength(200).HasColumnName("mailing_name");
            entity.Property(x => x.AddressLine1).HasMaxLength(250).HasColumnName("address_line1");
            entity.Property(x => x.AddressLine2).HasMaxLength(250).HasColumnName("address_line2");
            entity.Property(x => x.City).HasMaxLength(100).HasColumnName("city");
            entity.Property(x => x.State).HasMaxLength(100).HasColumnName("state");
            entity.Property(x => x.Country).HasMaxLength(100).HasColumnName("country");
            entity.Property(x => x.PinCode).HasMaxLength(20).HasColumnName("pin_code");
            entity.Property(x => x.ContactPerson).HasMaxLength(150).HasColumnName("contact_person");
            entity.Property(x => x.Phone).HasMaxLength(50).HasColumnName("phone");
            entity.Property(x => x.Mobile).HasMaxLength(50).HasColumnName("mobile");
            entity.Property(x => x.Email).HasMaxLength(200).HasColumnName("email");
            entity.Property(x => x.GstRegistrationType).HasMaxLength(50).HasColumnName("gst_registration_type");
            entity.Property(x => x.Gstin).HasMaxLength(15).HasColumnName("gstin");
            entity.Property(x => x.Pan).HasMaxLength(10).HasColumnName("pan");
            entity.Property(x => x.MaintainBillWise).HasColumnName("maintain_bill_wise");
            entity.Property(x => x.CreditPeriodDays).HasColumnName("credit_period_days");
            entity.Property(x => x.CreditLimit).HasPrecision(19, 4).HasColumnName("credit_limit");
            entity.Property(x => x.OpeningBalance).HasPrecision(19, 4).HasColumnName("opening_balance");
            entity.Property(x => x.OpeningBalanceType).HasMaxLength(2).HasColumnName("opening_balance_type");
            entity.Property(x => x.BankAccountNumber).HasMaxLength(50).HasColumnName("bank_account_number");
            entity.Property(x => x.BankIfsc).HasMaxLength(20).HasColumnName("bank_ifsc");
            entity.Property(x => x.BankName).HasMaxLength(150).HasColumnName("bank_name");
            entity.Property(x => x.BankBranch).HasMaxLength(150).HasColumnName("bank_branch");
            entity.Property(x => x.IsJobWorker).HasColumnName("is_job_worker");
            entity.Property(x => x.TallyLedgerName).HasMaxLength(200).HasColumnName("tally_ledger_name");
            entity.Property(x => x.DefaultMaterialOutDestinationGodownId).HasColumnName("default_material_out_destination_godown_id");
            entity.Property(x => x.DefaultMaterialInConsumptionGodownId).HasColumnName("default_material_in_consumption_godown_id");
            entity.HasOne(x => x.LedgerGroup)
                .WithMany()
                .HasForeignKey(x => x.LedgerGroupId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.DefaultMaterialOutDestinationGodown)
                .WithMany()
                .HasForeignKey(x => x.DefaultMaterialOutDestinationGodownId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.DefaultMaterialInConsumptionGodown)
                .WithMany()
                .HasForeignKey(x => x.DefaultMaterialInConsumptionGodownId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.CompanyId, x.LedgerGroupId });
            entity.HasIndex(x => new { x.CompanyId, x.Gstin });
            entity.HasIndex(x => new { x.CompanyId, x.IsJobWorker, x.NameNormalized });
            entity.HasIndex(x => x.DefaultMaterialOutDestinationGodownId);
            entity.HasIndex(x => x.DefaultMaterialInConsumptionGodownId);
        });

        modelBuilder.Entity<StockGroup>(entity =>
        {
            entity.Property(x => x.RootClassification).HasMaxLength(80).HasColumnName("root_classification");
            entity.Property(x => x.ParentId).HasColumnName("parent_id");
            entity.HasOne(x => x.Parent)
                .WithMany(x => x.Children)
                .HasForeignKey(x => x.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Uqc>(entity =>
        {
            entity.Property(x => x.ShortName).HasMaxLength(20).HasColumnName("short_name");
            entity.Property(x => x.DecimalPlaces).HasColumnName("decimal_places");
        });

        modelBuilder.Entity<Godown>(entity =>
        {
            entity.Property(x => x.AddressLine1).HasMaxLength(250).HasColumnName("address_line1");
            entity.Property(x => x.AddressLine2).HasMaxLength(250).HasColumnName("address_line2");
            entity.Property(x => x.City).HasMaxLength(100).HasColumnName("city");
            entity.Property(x => x.State).HasMaxLength(100).HasColumnName("state");
        });

        modelBuilder.Entity<Colour>(entity =>
        {
            entity.Property(x => x.ColourCode).HasMaxLength(30).HasColumnName("colour_code");
        });

        modelBuilder.Entity<SizeMaster>(entity =>
        {
            entity.Property(x => x.DisplayOrder).HasColumnName("display_order");
            entity.HasIndex(x => new { x.CompanyId, x.DisplayOrder });
        });

        modelBuilder.Entity<ProcessMaster>(entity =>
        {
            entity.Property(x => x.DisplayOrder).HasColumnName("display_order");
            entity.HasIndex(x => new { x.CompanyId, x.DisplayOrder });
        });

        modelBuilder.Entity<TaxClassification>(entity =>
        {
            entity.Property(x => x.TaxMode).HasMaxLength(40).HasColumnName("tax_mode");
        });

        modelBuilder.Entity<StockItem>(entity =>
        {
            entity.Property(x => x.StockGroupId).HasColumnName("stock_group_id");
            entity.Property(x => x.UqcId).HasColumnName("uqc_id");
            entity.Property(x => x.StockCategoryId).HasColumnName("stock_category_id");
            entity.Property(x => x.TaxMode).HasMaxLength(40).HasColumnName("tax_mode");
            entity.Property(x => x.TaxClassificationId).HasColumnName("tax_classification_id");
            entity.Property(x => x.HsnCode).HasMaxLength(20).HasColumnName("hsn_code");
            entity.Property(x => x.IgstRate).HasPrecision(7, 4).HasColumnName("igst_rate");
            entity.Property(x => x.CgstRate).HasPrecision(7, 4).HasColumnName("cgst_rate");
            entity.Property(x => x.SgstRate).HasPrecision(7, 4).HasColumnName("sgst_rate");
            entity.Property(x => x.CostPrice).HasPrecision(19, 4).HasColumnName("cost_price");
            entity.Property(x => x.SalePrice).HasPrecision(19, 4).HasColumnName("sale_price");
            entity.HasOne(x => x.StockGroup).WithMany().HasForeignKey(x => x.StockGroupId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Uqc).WithMany().HasForeignKey(x => x.UqcId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.StockCategory).WithMany().HasForeignKey(x => x.StockCategoryId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.TaxClassification).WithMany().HasForeignKey(x => x.TaxClassificationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.CompanyId, x.StockGroupId });
            entity.HasIndex(x => new { x.CompanyId, x.UqcId });
            entity.HasIndex(x => new { x.CompanyId, x.HsnCode });
        });

        modelBuilder.Entity<StockItemColour>(entity =>
        {
            entity.ToTable("stock_item_colours");
            entity.HasKey(x => new { x.StockItemId, x.ColourId });
            entity.Property(x => x.StockItemId).HasColumnName("stock_item_id");
            entity.Property(x => x.ColourId).HasColumnName("colour_id");
            entity.HasOne(x => x.StockItem).WithMany(x => x.Colours).HasForeignKey(x => x.StockItemId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Colour).WithMany().HasForeignKey(x => x.ColourId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => x.ColourId);
        });

        modelBuilder.Entity<StockItemSize>(entity =>
        {
            entity.ToTable("stock_item_sizes");
            entity.HasKey(x => new { x.StockItemId, x.SizeId });
            entity.Property(x => x.StockItemId).HasColumnName("stock_item_id");
            entity.Property(x => x.SizeId).HasColumnName("size_id");
            entity.HasOne(x => x.StockItem).WithMany(x => x.Sizes).HasForeignKey(x => x.StockItemId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Size).WithMany().HasForeignKey(x => x.SizeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => x.SizeId);
        });

        modelBuilder.Entity<StockItemVariant>(entity =>
        {
            entity.ToTable("stock_item_variants");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.CompanyId).HasColumnName("company_id");
            entity.Property(x => x.StockItemId).HasColumnName("stock_item_id");
            entity.Property(x => x.ColourId).HasColumnName("colour_id");
            entity.Property(x => x.SizeId).HasColumnName("size_id");
            entity.Property(x => x.VariantKey).HasMaxLength(100).HasColumnName("variant_key");
            entity.Property(x => x.IsActive).HasColumnName("is_active");
            ConfigureAuditProperties(entity);
            entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.StockItem).WithMany(x => x.Variants).HasForeignKey(x => x.StockItemId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Colour).WithMany().HasForeignKey(x => x.ColourId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Size).WithMany().HasForeignKey(x => x.SizeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.StockItemId, x.VariantKey }).IsUnique();
            entity.HasIndex(x => new { x.CompanyId, x.ColourId, x.SizeId });
        });

        modelBuilder.Entity<StockItemFieldDefinition>(entity =>
        {
            entity.ToTable("stock_item_field_definitions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.CompanyId).HasColumnName("company_id");
            entity.Property(x => x.Name).HasMaxLength(120).HasColumnName("name");
            entity.Property(x => x.NameNormalized).HasMaxLength(120).HasColumnName("name_normalized");
            entity.Property(x => x.FieldType).HasMaxLength(20).HasColumnName("field_type");
            entity.Property(x => x.DisplayOrder).HasColumnName("display_order");
            entity.Property(x => x.IsRequired).HasColumnName("is_required");
            entity.Property(x => x.AllowMultiple).HasColumnName("allow_multiple");
            entity.Property(x => x.OptionsText).HasMaxLength(2000).HasColumnName("options_text");
            entity.Property(x => x.IsActive).HasColumnName("is_active");
            ConfigureAuditProperties(entity);
            entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.CompanyId, x.NameNormalized }).IsUnique();
            entity.HasIndex(x => new { x.CompanyId, x.IsActive, x.DisplayOrder });
        });

        modelBuilder.Entity<StockItemFieldValue>(entity =>
        {
            entity.ToTable("stock_item_field_values");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.CompanyId).HasColumnName("company_id");
            entity.Property(x => x.StockItemId).HasColumnName("stock_item_id");
            entity.Property(x => x.FieldDefinitionId).HasColumnName("field_definition_id");
            entity.Property(x => x.Value).HasMaxLength(4000).HasColumnName("value");
            ConfigureAuditProperties(entity);
            entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.StockItem).WithMany(x => x.FieldValues).HasForeignKey(x => x.StockItemId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.FieldDefinition).WithMany(x => x.Values).HasForeignKey(x => x.FieldDefinitionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.StockItemId, x.FieldDefinitionId }).IsUnique();
            entity.HasIndex(x => new { x.CompanyId, x.FieldDefinitionId, x.Value });
        });

        modelBuilder.Entity<StockItemPhoto>(entity =>
        {
            entity.ToTable("stock_item_photos");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.CompanyId).HasColumnName("company_id");
            entity.Property(x => x.StockItemId).HasColumnName("stock_item_id");
            entity.Property(x => x.FieldDefinitionId).HasColumnName("field_definition_id");
            entity.Property(x => x.FileName).HasMaxLength(255).HasColumnName("file_name");
            entity.Property(x => x.ContentType).HasMaxLength(80).HasColumnName("content_type");
            entity.Property(x => x.Content).HasColumnName("content");
            entity.Property(x => x.DisplayOrder).HasColumnName("display_order");
            ConfigureAuditProperties(entity);
            entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.StockItem).WithMany(x => x.Photos).HasForeignKey(x => x.StockItemId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.FieldDefinition).WithMany(x => x.Photos).HasForeignKey(x => x.FieldDefinitionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.StockItemId, x.FieldDefinitionId, x.DisplayOrder }).IsUnique();
        });

        modelBuilder.Entity<StockItemDesignPhoto>(entity =>
        {
            entity.ToTable("stock_item_design_photos");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.CompanyId).HasColumnName("company_id");
            entity.Property(x => x.StockItemId).HasColumnName("stock_item_id");
            entity.Property(x => x.FileName).HasMaxLength(255).HasColumnName("file_name");
            entity.Property(x => x.ContentType).HasMaxLength(80).HasColumnName("content_type");
            entity.Property(x => x.Content).HasColumnName("content");
            ConfigureAuditProperties(entity);
            entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.StockItem).WithOne(x => x.DesignPhoto).HasForeignKey<StockItemDesignPhoto>(x => x.StockItemId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => x.StockItemId).IsUnique();
        });

        modelBuilder.Entity<VoucherType>(entity =>
        {
            entity.Property(x => x.SystemTypeCode).HasMaxLength(60).HasColumnName("system_type_code");
            entity.Property(x => x.Nature).HasMaxLength(100).HasColumnName("nature");
            entity.Property(x => x.PostingMode).HasMaxLength(100).HasColumnName("posting_mode");
            entity.Property(x => x.Abbreviation).HasMaxLength(20).HasColumnName("abbreviation");
            entity.Property(x => x.AllowManualNumbering).HasColumnName("allow_manual_numbering");
            entity.Property(x => x.NumberingMode).HasMaxLength(30).HasColumnName("numbering_mode");
            entity.Property(x => x.Prefix).HasMaxLength(30).HasColumnName("prefix");
            entity.Property(x => x.Suffix).HasMaxLength(30).HasColumnName("suffix");
            entity.Property(x => x.StartingNumber).HasColumnName("starting_number");
            entity.Property(x => x.NumberWidth).HasColumnName("number_width");
            entity.Property(x => x.ResetPeriod).HasMaxLength(30).HasColumnName("reset_period");
            entity.Property(x => x.TallyVoucherTypeName).HasMaxLength(200).HasColumnName("tally_voucher_type_name");
            entity.Property(x => x.ParentVoucherTypeId).HasColumnName("parent_voucher_type_id");
            entity.HasOne(x => x.ParentVoucherType)
                .WithMany(x => x.ChildVoucherTypes)
                .HasForeignKey(x => x.ParentVoucherTypeId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.CompanyId, x.SystemTypeCode }).IsUnique();
            entity.HasIndex(x => new { x.CompanyId, x.ParentVoucherTypeId, x.NameNormalized });
        });

        modelBuilder.Entity<Voucher>(entity =>
        {
            entity.ToTable("vouchers");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.AuditIdentity).HasColumnName("audit_identity")
                .HasDefaultValueSql("gen_random_uuid()").ValueGeneratedOnAdd();
            entity.Property(x => x.CompanyId).HasColumnName("company_id");
            entity.Property(x => x.FinancialYearId).HasColumnName("financial_year_id");
            entity.Property(x => x.VoucherTypeId).HasColumnName("voucher_type_id");
            entity.Property(x => x.SequenceNumber).HasColumnName("sequence_number");
            entity.Property(x => x.VoucherNumber).HasMaxLength(100).HasColumnName("voucher_number");
            entity.Property(x => x.VoucherNumberNormalized).HasMaxLength(100).HasColumnName("voucher_number_normalized");
            entity.Property(x => x.VoucherDate).HasColumnName("voucher_date");
            entity.Property(x => x.ReferenceNumber).HasMaxLength(100).HasColumnName("reference_number");
            entity.Property(x => x.Batch).HasMaxLength(100).HasColumnName("batch");
            entity.Property(x => x.PartyLedgerId).HasColumnName("party_ledger_id");
            entity.Property(x => x.OpeningStockItemId).HasColumnName("opening_stock_item_id");
            entity.Property(x => x.MasterJobOrderId).HasColumnName("master_job_order_id");
            entity.Property(x => x.DueDate).HasColumnName("due_date");
            entity.Property(x => x.Narration).HasMaxLength(1000).HasColumnName("narration");
            entity.Property(x => x.Status).HasMaxLength(30).HasColumnName("status");
            entity.Property(x => x.CancellationReason).HasMaxLength(500).HasColumnName("cancellation_reason");
            entity.Property(x => x.CancelledAtUtc).HasColumnName("cancelled_at_utc");
            entity.Property(x => x.CancelledBy).HasMaxLength(100).HasColumnName("cancelled_by");
            ConfigureAuditProperties(entity);
            entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.FinancialYear).WithMany().HasForeignKey(x => x.FinancialYearId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.VoucherType).WithMany().HasForeignKey(x => x.VoucherTypeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.PartyLedger).WithMany().HasForeignKey(x => x.PartyLedgerId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.OpeningStockItem).WithMany().HasForeignKey(x => x.OpeningStockItemId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.MasterJobOrder).WithMany(x => x.LinkedJobWorkOrders).HasForeignKey(x => x.MasterJobOrderId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.CompanyId, x.FinancialYearId, x.VoucherTypeId, x.VoucherNumberNormalized }).IsUnique();
            entity.HasIndex(x => new { x.CompanyId, x.FinancialYearId, x.VoucherTypeId, x.SequenceNumber }).IsUnique();
            entity.HasIndex(x => new { x.CompanyId, x.VoucherDate });
            entity.HasIndex(x => new { x.CompanyId, x.PartyLedgerId, x.Status });
            entity.HasIndex(x => new { x.CompanyId, x.Batch });
            entity.HasIndex(x => x.AuditIdentity).IsUnique();
            entity.HasIndex(x => new { x.CompanyId, x.FinancialYearId, x.OpeningStockItemId })
                .IsUnique()
                .HasFilter("opening_stock_item_id IS NOT NULL");
        });

        modelBuilder.Entity<JobWorkOrderFinishedGood>(entity =>
        {
            entity.ToTable("job_work_order_finished_goods");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.VoucherId).HasColumnName("voucher_id");
            entity.Property(x => x.LineNumber).HasColumnName("line_number");
            entity.Property(x => x.DesignGroupKey).HasMaxLength(64).HasColumnName("design_group_key");
            entity.Property(x => x.StockItemId).HasColumnName("stock_item_id");
            entity.Property(x => x.ColourId).HasColumnName("colour_id");
            entity.Property(x => x.OrderedQuantity).HasPrecision(19, 4).HasColumnName("ordered_quantity");
            entity.Property(x => x.FinishedGoodsGodownId).HasColumnName("finished_goods_godown_id");
            entity.Property(x => x.DestinationGodownId).HasColumnName("destination_godown_id");
            entity.Property(x => x.XmlRate).HasPrecision(19, 4).HasColumnName("xml_rate");
            entity.Property(x => x.XmlAmount).HasPrecision(19, 4).HasColumnName("xml_amount");
            entity.HasOne(x => x.Voucher).WithMany(x => x.JobWorkFinishedGoods).HasForeignKey(x => x.VoucherId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.StockItem).WithMany().HasForeignKey(x => x.StockItemId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Colour).WithMany().HasForeignKey(x => x.ColourId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.FinishedGoodsGodown).WithMany().HasForeignKey(x => x.FinishedGoodsGodownId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.DestinationGodown).WithMany().HasForeignKey(x => x.DestinationGodownId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.VoucherId, x.LineNumber }).IsUnique();
            entity.HasIndex(x => new { x.StockItemId, x.ColourId });
            entity.HasIndex(x => new { x.VoucherId, x.DesignGroupKey, x.LineNumber });
        });

        modelBuilder.Entity<JobWorkOrderSizeAllocation>(entity =>
        {
            entity.ToTable("job_work_order_size_allocations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.FinishedGoodId).HasColumnName("finished_good_id");
            entity.Property(x => x.StockItemVariantId).HasColumnName("stock_item_variant_id");
            entity.Property(x => x.SizeId).HasColumnName("size_id");
            entity.Property(x => x.Quantity).HasPrecision(19, 4).HasColumnName("quantity");
            entity.Property(x => x.MasterJobOrderAllocationId).HasColumnName("master_job_order_allocation_id");
            entity.HasOne(x => x.FinishedGood).WithMany(x => x.SizeAllocations).HasForeignKey(x => x.FinishedGoodId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.StockItemVariant).WithMany().HasForeignKey(x => x.StockItemVariantId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Size).WithMany().HasForeignKey(x => x.SizeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.MasterJobOrderAllocation).WithMany(x => x.JobWorkAllocations).HasForeignKey(x => x.MasterJobOrderAllocationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.FinishedGoodId, x.StockItemVariantId }).IsUnique();
            entity.HasIndex(x => x.MasterJobOrderAllocationId);
        });

        modelBuilder.Entity<MasterJobOrderFinishedGood>(entity =>
        {
            entity.ToTable("master_job_order_finished_goods");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.VoucherId).HasColumnName("voucher_id");
            entity.Property(x => x.LineNumber).HasColumnName("line_number");
            entity.Property(x => x.StockItemId).HasColumnName("stock_item_id");
            entity.Property(x => x.OrderedQuantity).HasPrecision(19, 4).HasColumnName("ordered_quantity");
            entity.HasOne(x => x.Voucher).WithMany(x => x.MasterJobOrderFinishedGoods).HasForeignKey(x => x.VoucherId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.StockItem).WithMany().HasForeignKey(x => x.StockItemId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.VoucherId, x.LineNumber }).IsUnique();
            entity.HasIndex(x => new { x.VoucherId, x.StockItemId }).IsUnique();
        });

        modelBuilder.Entity<MasterJobOrderAllocation>(entity =>
        {
            entity.ToTable("master_job_order_allocations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.FinishedGoodId).HasColumnName("finished_good_id");
            entity.Property(x => x.StockItemVariantId).HasColumnName("stock_item_variant_id");
            entity.Property(x => x.ColourId).HasColumnName("colour_id");
            entity.Property(x => x.SizeId).HasColumnName("size_id");
            entity.Property(x => x.Quantity).HasPrecision(19, 4).HasColumnName("quantity");
            entity.HasOne(x => x.FinishedGood).WithMany(x => x.Allocations).HasForeignKey(x => x.FinishedGoodId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.StockItemVariant).WithMany().HasForeignKey(x => x.StockItemVariantId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Colour).WithMany().HasForeignKey(x => x.ColourId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Size).WithMany().HasForeignKey(x => x.SizeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.FinishedGoodId, x.StockItemVariantId }).IsUnique();
        });

        modelBuilder.Entity<JobWorkOrderComponent>(entity =>
        {
            entity.ToTable("job_work_order_components");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.FinishedGoodId).HasColumnName("finished_good_id");
            entity.Property(x => x.LineNumber).HasColumnName("line_number");
            entity.Property(x => x.StockItemId).HasColumnName("stock_item_id");
            entity.Property(x => x.UqcId).HasColumnName("uqc_id");
            entity.Property(x => x.RequiredQuantity).HasPrecision(19, 4).HasColumnName("required_quantity");
            entity.Property(x => x.ComponentGodownId).HasColumnName("component_godown_id");
            entity.Property(x => x.XmlRate).HasPrecision(19, 4).HasColumnName("xml_rate");
            entity.Property(x => x.XmlAmount).HasPrecision(19, 4).HasColumnName("xml_amount");
            entity.Property(x => x.BomStageId).HasColumnName("bom_stage_id");
            entity.Property(x => x.ParentComponentId).HasColumnName("parent_component_id");
            entity.Property(x => x.ChildBomStageId).HasColumnName("child_bom_stage_id");
            entity.Property(x => x.ComponentVariantId).HasColumnName("component_variant_id");
            entity.Property(x => x.BomLevel).HasColumnName("bom_level");
            entity.Property(x => x.BomPath).HasMaxLength(500).HasColumnName("bom_path");
            entity.Property(x => x.IsProducedComponent).HasColumnName("is_produced_component");
            entity.HasOne(x => x.FinishedGood).WithMany(x => x.Components).HasForeignKey(x => x.FinishedGoodId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.StockItem).WithMany().HasForeignKey(x => x.StockItemId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Uqc).WithMany().HasForeignKey(x => x.UqcId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ComponentGodown).WithMany().HasForeignKey(x => x.ComponentGodownId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.BomStage).WithMany(x => x.Components).HasForeignKey(x => x.BomStageId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.ParentComponent).WithMany(x => x.ChildComponents).HasForeignKey(x => x.ParentComponentId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.ChildBomStage).WithMany(x => x.ProducedComponents).HasForeignKey(x => x.ChildBomStageId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ComponentVariant).WithMany().HasForeignKey(x => x.ComponentVariantId).OnDelete(DeleteBehavior.Restrict);
            // PostgreSQL uses uq_jwo_component_stage_line with COALESCE(bom_stage_id, 0).
            // EF cannot model that expression index; do not declare a conflicting index here.
        });

        // BUILD 2.10
        modelBuilder.Entity<JobWorkOrderProcess>(entity =>
        {
            entity.ToTable("job_work_order_processes");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.FinishedGoodId).HasColumnName("finished_good_id");
            entity.Property(x => x.ProcessId).HasColumnName("process_id");
            entity.Property(x => x.ExpectedRate).HasPrecision(19, 4).HasColumnName("expected_rate");
            entity.Property(x => x.RateBasis).HasMaxLength(50).HasColumnName("rate_basis");
            entity.HasOne(x => x.FinishedGood).WithMany(x => x.Processes).HasForeignKey(x => x.FinishedGoodId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Process).WithMany().HasForeignKey(x => x.ProcessId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.FinishedGoodId, x.ProcessId }).IsUnique();
        });

        modelBuilder.Entity<MaterialOutLine>(entity =>
        {
            entity.ToTable("material_out_lines");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.VoucherId).HasColumnName("voucher_id");
            entity.Property(x => x.JwoVoucherId).HasColumnName("jwo_voucher_id");
            entity.Property(x => x.JwoFinishedGoodId).HasColumnName("jwo_finished_good_id");
            entity.Property(x => x.JwoComponentId).HasColumnName("jwo_component_id");
            entity.Property(x => x.BomStageId).HasColumnName("bom_stage_id");
            entity.Property(x => x.StageAssignmentId).HasColumnName("stage_assignment_id");
            entity.Property(x => x.LineNumber).HasColumnName("line_number");
            entity.Property(x => x.StockItemId).HasColumnName("stock_item_id");
            entity.Property(x => x.UqcId).HasColumnName("uqc_id");
            entity.Property(x => x.SourceGodownId).HasColumnName("source_godown_id");
            entity.Property(x => x.DestinationGodownId).HasColumnName("destination_godown_id");
            entity.Property(x => x.RequiredQuantity).HasPrecision(19, 4).HasColumnName("required_quantity");
            entity.Property(x => x.PreviouslyIssuedQuantity).HasPrecision(19, 4).HasColumnName("previously_issued_quantity");
            entity.Property(x => x.IssuedQuantity).HasPrecision(19, 4).HasColumnName("issued_quantity");
            entity.Property(x => x.Rate).HasPrecision(19, 4).HasColumnName("rate");
            entity.Property(x => x.Amount).HasPrecision(19, 4).HasColumnName("amount");
            entity.HasOne(x => x.Voucher).WithMany(x => x.MaterialOutLines).HasForeignKey(x => x.VoucherId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.JwoVoucher).WithMany().HasForeignKey(x => x.JwoVoucherId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.JwoFinishedGood).WithMany().HasForeignKey(x => x.JwoFinishedGoodId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.JwoComponent).WithMany().HasForeignKey(x => x.JwoComponentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.BomStage).WithMany().HasForeignKey(x => x.BomStageId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.StageAssignment).WithMany().HasForeignKey(x => x.StageAssignmentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.StockItem).WithMany().HasForeignKey(x => x.StockItemId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Uqc).WithMany().HasForeignKey(x => x.UqcId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.SourceGodown).WithMany().HasForeignKey(x => x.SourceGodownId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.DestinationGodown).WithMany().HasForeignKey(x => x.DestinationGodownId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.VoucherId, x.LineNumber }).IsUnique();
            entity.HasIndex(x => new { x.JwoVoucherId, x.JwoComponentId, x.VoucherId });
            entity.HasIndex(x => new { x.BomStageId, x.VoucherId });
        });

        modelBuilder.Entity<MaterialOutDetail>(entity =>
        {
            entity.ToTable("material_out_details");
            entity.HasKey(x => x.VoucherId);
            entity.Property(x => x.VoucherId).HasColumnName("voucher_id");
            entity.Property(x => x.ProvideGstEwayDetails).HasColumnName("provide_gst_eway_details");
            entity.Property(x => x.DestinationGodownId).HasColumnName("destination_godown_id");
            entity.Property(x => x.DisplayedOrderNumber).HasMaxLength(100).HasColumnName("displayed_order_number");
            entity.Property(x => x.EwayBillNumber).HasMaxLength(30).HasColumnName("eway_bill_number");
            entity.Property(x => x.EwayBillDate).HasColumnName("eway_bill_date");
            entity.Property(x => x.ConsolidatedEwayBillNumber).HasMaxLength(30).HasColumnName("consolidated_eway_bill_number");
            entity.Property(x => x.ConsolidatedEwayBillDate).HasColumnName("consolidated_eway_bill_date");
            entity.Property(x => x.EwaySubType).HasMaxLength(50).HasColumnName("eway_sub_type");
            entity.Property(x => x.EwayDocumentType).HasMaxLength(50).HasColumnName("eway_document_type");
            entity.Property(x => x.ConsignorMailingName).HasMaxLength(200).HasColumnName("consignor_mailing_name");
            entity.Property(x => x.ConsignorGstin).HasMaxLength(20).HasColumnName("consignor_gstin");
            entity.Property(x => x.ConsignorState).HasMaxLength(100).HasColumnName("consignor_state");
            entity.Property(x => x.ConsignorAddress1).HasMaxLength(250).HasColumnName("consignor_address1");
            entity.Property(x => x.ConsignorAddress2).HasMaxLength(250).HasColumnName("consignor_address2");
            entity.Property(x => x.ConsignorPincode).HasMaxLength(10).HasColumnName("consignor_pincode");
            entity.Property(x => x.ConsignorPlace).HasMaxLength(100).HasColumnName("consignor_place");
            entity.Property(x => x.ConsignorActualState).HasMaxLength(100).HasColumnName("consignor_actual_state");
            entity.Property(x => x.ConsigneeMailingName).HasMaxLength(200).HasColumnName("consignee_mailing_name");
            entity.Property(x => x.ConsigneeGstin).HasMaxLength(20).HasColumnName("consignee_gstin");
            entity.Property(x => x.ConsigneeState).HasMaxLength(100).HasColumnName("consignee_state");
            entity.Property(x => x.ConsigneeAddress1).HasMaxLength(250).HasColumnName("consignee_address1");
            entity.Property(x => x.ConsigneeAddress2).HasMaxLength(250).HasColumnName("consignee_address2");
            entity.Property(x => x.ConsigneePincode).HasMaxLength(10).HasColumnName("consignee_pincode");
            entity.Property(x => x.ConsigneePlace).HasMaxLength(100).HasColumnName("consignee_place");
            entity.Property(x => x.ConsigneeActualState).HasMaxLength(100).HasColumnName("consignee_actual_state");
            entity.Property(x => x.PinToPinDistance).HasMaxLength(20).HasColumnName("pin_to_pin_distance");
            entity.Property(x => x.TransporterName).HasMaxLength(200).HasColumnName("transporter_name");
            entity.Property(x => x.TransporterId).HasMaxLength(30).HasColumnName("transporter_id");
            entity.Property(x => x.TransportMode).HasMaxLength(40).HasColumnName("transport_mode");
            entity.Property(x => x.TransportDocumentNumber).HasMaxLength(50).HasColumnName("transport_document_number");
            entity.Property(x => x.TransportDocumentDate).HasColumnName("transport_document_date");
            entity.Property(x => x.VehicleNumber).HasMaxLength(30).HasColumnName("vehicle_number");
            entity.Property(x => x.VehicleType).HasMaxLength(50).HasColumnName("vehicle_type");
            entity.HasOne(x => x.Voucher).WithOne(x => x.MaterialOutDetail).HasForeignKey<MaterialOutDetail>(x => x.VoucherId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.DestinationGodown).WithMany().HasForeignKey(x => x.DestinationGodownId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<MaterialInDetail>(entity =>
        {
            entity.ToTable("material_in_details");
            entity.HasKey(x => x.VoucherId);
            entity.Property(x => x.VoucherId).HasColumnName("voucher_id");
            entity.Property(x => x.JwoVoucherId).HasColumnName("jwo_voucher_id");
            entity.Property(x => x.ConsumptionGodownId).HasColumnName("consumption_godown_id");
            entity.Property(x => x.ReceivingGodownId).HasColumnName("receiving_godown_id");
            entity.Property(x => x.DisplayedOrderNumber).HasMaxLength(100).HasColumnName("displayed_order_number");
            entity.Property(x => x.TotalProcessCharge).HasPrecision(19, 4).HasColumnName("total_process_charge");
            entity.Property(x => x.TotalConsumedMaterialValue).HasPrecision(19, 4).HasColumnName("total_consumed_material_value");
            entity.Property(x => x.TotalFinishedGoodsValue).HasPrecision(19, 4).HasColumnName("total_finished_goods_value");
            entity.HasOne(x => x.Voucher).WithOne(x => x.MaterialInDetail).HasForeignKey<MaterialInDetail>(x => x.VoucherId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.JwoVoucher).WithMany().HasForeignKey(x => x.JwoVoucherId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ConsumptionGodown).WithMany().HasForeignKey(x => x.ConsumptionGodownId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ReceivingGodown).WithMany().HasForeignKey(x => x.ReceivingGodownId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.JwoVoucherId, x.VoucherId });
        });

        modelBuilder.Entity<MaterialInFinishedGood>(entity =>
        {
            entity.ToTable("material_in_finished_goods");
            entity.Property(x => x.ExpectedProcessRate).HasPrecision(19,4).HasColumnName("expected_process_rate");
            entity.Property(x => x.ActualProcessRate).HasPrecision(19,4).HasColumnName("actual_process_rate");
            entity.Property(x => x.ChargeMode).HasMaxLength(16).HasColumnName("charge_mode");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.VoucherId).HasColumnName("voucher_id");
            entity.Property(x => x.JwoFinishedGoodId).HasColumnName("jwo_finished_good_id");
            entity.Property(x => x.LineNumber).HasColumnName("line_number");
            entity.Property(x => x.StockItemId).HasColumnName("stock_item_id");
            entity.Property(x => x.UqcId).HasColumnName("uqc_id");
            entity.Property(x => x.ReceivingGodownId).HasColumnName("receiving_godown_id");
            entity.Property(x => x.OrderedQuantity).HasPrecision(19,4).HasColumnName("ordered_quantity");
            entity.Property(x => x.PreviouslyReceivedQuantity).HasPrecision(19,4).HasColumnName("previously_received_quantity");
            entity.Property(x => x.ReceivedQuantity).HasPrecision(19,4).HasColumnName("received_quantity");
            entity.Property(x => x.MaterialValue).HasPrecision(19,4).HasColumnName("material_value");
            entity.Property(x => x.ProcessCharge).HasPrecision(19,4).HasColumnName("process_charge");
            entity.Property(x => x.FinishedGoodsValue).HasPrecision(19,4).HasColumnName("finished_goods_value");
            entity.Property(x => x.Rate).HasPrecision(19,4).HasColumnName("rate");
            entity.Property(x => x.BomStageId).HasColumnName("bom_stage_id");
            entity.Property(x => x.StageAssignmentId).HasColumnName("stage_assignment_id");
            entity.HasOne(x => x.Voucher).WithMany(x => x.MaterialInFinishedGoods).HasForeignKey(x => x.VoucherId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.JwoFinishedGood).WithMany().HasForeignKey(x => x.JwoFinishedGoodId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.StockItem).WithMany().HasForeignKey(x => x.StockItemId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Uqc).WithMany().HasForeignKey(x => x.UqcId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ReceivingGodown).WithMany().HasForeignKey(x => x.ReceivingGodownId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.BomStage).WithMany(x => x.MaterialInFinishedGoods).HasForeignKey(x => x.BomStageId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.StageAssignment).WithMany().HasForeignKey(x => x.StageAssignmentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.VoucherId, x.LineNumber }).IsUnique();
            entity.HasIndex(x => new { x.VoucherId, x.JwoFinishedGoodId }).IsUnique();
        });

        modelBuilder.Entity<MaterialInFinishedGoodAllocation>(entity =>
        {
            entity.ToTable("material_in_fg_allocations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.FinishedGoodLineId).HasColumnName("finished_good_line_id");
            entity.Property(x => x.StockItemVariantId).HasColumnName("stock_item_variant_id");
            entity.Property(x => x.Quantity).HasPrecision(19,4).HasColumnName("quantity");
            entity.HasOne(x => x.FinishedGoodLine).WithMany(x => x.Allocations).HasForeignKey(x => x.FinishedGoodLineId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.StockItemVariant).WithMany().HasForeignKey(x => x.StockItemVariantId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.FinishedGoodLineId, x.StockItemVariantId }).IsUnique();
        });

        modelBuilder.Entity<MaterialInConsumption>(entity =>
        {
            entity.ToTable("material_in_consumptions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.VoucherId).HasColumnName("voucher_id");
            entity.Property(x => x.JwoComponentId).HasColumnName("jwo_component_id");
            entity.Property(x => x.BomStageId).HasColumnName("bom_stage_id");
            entity.Property(x => x.StageAssignmentId).HasColumnName("stage_assignment_id");
            entity.Property(x => x.LineNumber).HasColumnName("line_number");
            entity.Property(x => x.StockItemId).HasColumnName("stock_item_id");
            entity.Property(x => x.UqcId).HasColumnName("uqc_id");
            entity.Property(x => x.ConsumptionGodownId).HasColumnName("consumption_godown_id");
            entity.Property(x => x.AvailableQuantity).HasPrecision(19,4).HasColumnName("available_quantity");
            entity.Property(x => x.ConsumedQuantity).HasPrecision(19,4).HasColumnName("consumed_quantity");
            entity.Property(x => x.Rate).HasPrecision(19,4).HasColumnName("rate");
            entity.Property(x => x.Value).HasPrecision(19,4).HasColumnName("value");
            entity.HasOne(x => x.Voucher).WithMany(x => x.MaterialInConsumptions).HasForeignKey(x => x.VoucherId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.JwoComponent).WithMany().HasForeignKey(x => x.JwoComponentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.BomStage).WithMany().HasForeignKey(x => x.BomStageId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.StageAssignment).WithMany().HasForeignKey(x => x.StageAssignmentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.StockItem).WithMany().HasForeignKey(x => x.StockItemId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Uqc).WithMany().HasForeignKey(x => x.UqcId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ConsumptionGodown).WithMany().HasForeignKey(x => x.ConsumptionGodownId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.VoucherId, x.LineNumber }).IsUnique();
            entity.HasIndex(x => new { x.VoucherId, x.JwoComponentId }).IsUnique();
        });

        modelBuilder.Entity<MaterialInMaterialOutAllocation>(entity =>
        {
            entity.ToTable("material_in_mo_allocations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.ConsumptionLineId).HasColumnName("consumption_line_id");
            entity.Property(x => x.MaterialOutLineId).HasColumnName("material_out_line_id");
            entity.Property(x => x.AllocatedQuantity).HasPrecision(19,4).HasColumnName("allocated_quantity");
            entity.Property(x => x.RateSnapshot).HasPrecision(19,4).HasColumnName("rate_snapshot");
            entity.Property(x => x.ValueSnapshot).HasPrecision(19,4).HasColumnName("value_snapshot");
            entity.HasOne(x => x.ConsumptionLine).WithMany(x => x.MaterialOutAllocations).HasForeignKey(x => x.ConsumptionLineId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.MaterialOutLine).WithMany().HasForeignKey(x => x.MaterialOutLineId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.ConsumptionLineId, x.MaterialOutLineId }).IsUnique();
            entity.HasIndex(x => new { x.MaterialOutLineId, x.ConsumptionLineId });
        });

        modelBuilder.Entity<InventoryInwardLine>(entity =>
        {
            entity.ToTable("inventory_inward_lines");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.VoucherId).HasColumnName("voucher_id");
            entity.Property(x => x.LineNumber).HasColumnName("line_number");
            entity.Property(x => x.StockItemId).HasColumnName("stock_item_id");
            entity.Property(x => x.StockItemVariantId).HasColumnName("stock_item_variant_id");
            entity.Property(x => x.UqcId).HasColumnName("uqc_id");
            entity.Property(x => x.GodownId).HasColumnName("godown_id");
            entity.Property(x => x.Quantity).HasPrecision(19, 4).HasColumnName("quantity");
            entity.Property(x => x.Rate).HasPrecision(19, 4).HasColumnName("rate");
            entity.Property(x => x.Amount).HasPrecision(19, 4).HasColumnName("amount");
            entity.Property(x => x.PurchaseOrderLineId).HasColumnName("purchase_order_line_id");
            entity.HasOne(x => x.Voucher).WithMany(x => x.InventoryInwardLines)
                .HasForeignKey(x => x.VoucherId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.StockItem).WithMany().HasForeignKey(x => x.StockItemId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.StockItemVariant).WithMany().HasForeignKey(x => x.StockItemVariantId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Uqc).WithMany().HasForeignKey(x => x.UqcId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Godown).WithMany().HasForeignKey(x => x.GodownId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.PurchaseOrderLine).WithMany().HasForeignKey(x => x.PurchaseOrderLineId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.VoucherId, x.LineNumber }).IsUnique();
            // Includes PurchaseOrderLineId so the same item/variant/godown position can appear
            // twice when each line is tagged to a different Purchase Order line (one invoice
            // covering several POs) - ValidateAndNormalizeLinesAsync is still the authoritative
            // gate for the open-market (both-null) case, since Postgres treats NULLs as distinct
            // in a unique index and won't reject that combination on its own here. The raw SQL
            // migration (040) enforces the null case too via a COALESCE expression index, which
            // this fluent mapping doesn't replicate exactly.
            entity.HasIndex(x => new { x.VoucherId, x.StockItemId, x.StockItemVariantId, x.UqcId, x.GodownId, x.PurchaseOrderLineId }).IsUnique();
            entity.HasIndex(x => new { x.StockItemId, x.StockItemVariantId, x.UqcId, x.GodownId, x.VoucherId });
            entity.HasIndex(x => x.PurchaseOrderLineId);
        });

        modelBuilder.Entity<PurchaseOrderLine>(entity =>
        {
            entity.ToTable("purchase_order_lines");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.VoucherId).HasColumnName("voucher_id");
            entity.Property(x => x.LineNumber).HasColumnName("line_number");
            entity.Property(x => x.StockItemId).HasColumnName("stock_item_id");
            entity.Property(x => x.StockItemVariantId).HasColumnName("stock_item_variant_id");
            entity.Property(x => x.UqcId).HasColumnName("uqc_id");
            entity.Property(x => x.GodownId).HasColumnName("godown_id");
            entity.Property(x => x.OrderedQuantity).HasPrecision(19, 4).HasColumnName("ordered_quantity");
            entity.Property(x => x.Rate).HasPrecision(19, 4).HasColumnName("rate");
            entity.Property(x => x.Amount).HasPrecision(19, 4).HasColumnName("amount");
            entity.Property(x => x.ExpectedDeliveryDate).HasColumnName("expected_delivery_date");
            entity.HasOne(x => x.Voucher).WithMany(x => x.PurchaseOrderLines)
                .HasForeignKey(x => x.VoucherId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.StockItem).WithMany().HasForeignKey(x => x.StockItemId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.StockItemVariant).WithMany().HasForeignKey(x => x.StockItemVariantId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Uqc).WithMany().HasForeignKey(x => x.UqcId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Godown).WithMany().HasForeignKey(x => x.GodownId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.VoucherId, x.LineNumber }).IsUnique();
            entity.HasIndex(x => new { x.VoucherId, x.StockItemId, x.StockItemVariantId, x.UqcId, x.GodownId }).IsUnique();
            entity.HasIndex(x => new { x.StockItemId, x.StockItemVariantId, x.UqcId, x.VoucherId });
        });

        modelBuilder.Entity<PurchaseReturnLine>(entity =>
        {
            entity.ToTable("purchase_return_lines");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.VoucherId).HasColumnName("voucher_id");
            entity.Property(x => x.LineNumber).HasColumnName("line_number");
            entity.Property(x => x.StockItemId).HasColumnName("stock_item_id");
            entity.Property(x => x.StockItemVariantId).HasColumnName("stock_item_variant_id");
            entity.Property(x => x.UqcId).HasColumnName("uqc_id");
            entity.Property(x => x.GodownId).HasColumnName("godown_id");
            entity.Property(x => x.Quantity).HasPrecision(19, 4).HasColumnName("quantity");
            entity.Property(x => x.Rate).HasPrecision(19, 4).HasColumnName("rate");
            entity.Property(x => x.Amount).HasPrecision(19, 4).HasColumnName("amount");
            entity.HasOne(x => x.Voucher).WithMany(x => x.PurchaseReturnLines)
                .HasForeignKey(x => x.VoucherId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.StockItem).WithMany().HasForeignKey(x => x.StockItemId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.StockItemVariant).WithMany().HasForeignKey(x => x.StockItemVariantId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Uqc).WithMany().HasForeignKey(x => x.UqcId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Godown).WithMany().HasForeignKey(x => x.GodownId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.VoucherId, x.LineNumber }).IsUnique();
            entity.HasIndex(x => new { x.VoucherId, x.StockItemId, x.StockItemVariantId, x.UqcId, x.GodownId }).IsUnique();
            entity.HasIndex(x => new { x.StockItemId, x.StockItemVariantId, x.UqcId, x.GodownId, x.VoucherId });
        });

        modelBuilder.Entity<StockMovement>(entity =>
        {
            entity.ToTable("stock_movements");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.CompanyId).HasColumnName("company_id");
            entity.Property(x => x.FinancialYearId).HasColumnName("financial_year_id");
            entity.Property(x => x.VoucherId).HasColumnName("voucher_id");
            entity.Property(x => x.MaterialOutLineId).HasColumnName("material_out_line_id");
            entity.Property(x => x.MaterialInFinishedGoodId).HasColumnName("material_in_finished_good_id");
            entity.Property(x => x.MaterialInConsumptionId).HasColumnName("material_in_consumption_id");
            entity.Property(x => x.InventoryInwardLineId).HasColumnName("inventory_inward_line_id");
            entity.Property(x => x.PurchaseReturnLineId).HasColumnName("purchase_return_line_id");
            entity.Property(x => x.InventoryPostingId).HasColumnName("inventory_posting_id");
            entity.Property(x => x.MovementLineOrder).HasColumnName("movement_line_order");
            entity.Property(x => x.StockItemVariantId).HasColumnName("stock_item_variant_id");
            entity.Property(x => x.MovementDate).HasColumnName("movement_date");
            entity.Property(x => x.StockItemId).HasColumnName("stock_item_id");
            entity.Property(x => x.UqcId).HasColumnName("uqc_id");
            entity.Property(x => x.GodownId).HasColumnName("godown_id");
            entity.Property(x => x.QuantityChange).HasPrecision(19, 4).HasColumnName("quantity_change");
            entity.Property(x => x.Rate).HasPrecision(19, 4).HasColumnName("rate");
            entity.Property(x => x.ValueChange).HasPrecision(19, 4).HasColumnName("value_change");
            entity.Property(x => x.MovementKind).HasMaxLength(50).HasColumnName("movement_kind");
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(x => x.CreatedBy).HasMaxLength(100).HasColumnName("created_by");
            entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.FinancialYear).WithMany().HasForeignKey(x => x.FinancialYearId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Voucher).WithMany().HasForeignKey(x => x.VoucherId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.MaterialOutLine).WithMany().HasForeignKey(x => x.MaterialOutLineId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.MaterialInFinishedGood).WithMany().HasForeignKey(x => x.MaterialInFinishedGoodId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.MaterialInConsumption).WithMany().HasForeignKey(x => x.MaterialInConsumptionId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.InventoryInwardLine).WithMany().HasForeignKey(x => x.InventoryInwardLineId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.PurchaseReturnLine).WithMany().HasForeignKey(x => x.PurchaseReturnLineId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.InventoryPosting).WithMany().HasForeignKey(x => x.InventoryPostingId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.StockItemVariant).WithMany().HasForeignKey(x => x.StockItemVariantId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.StockItem).WithMany().HasForeignKey(x => x.StockItemId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Uqc).WithMany().HasForeignKey(x => x.UqcId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Godown).WithMany().HasForeignKey(x => x.GodownId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.CompanyId, x.StockItemId, x.GodownId, x.MovementDate, x.Id });
            entity.HasIndex(x => new { x.VoucherId, x.MaterialOutLineId });
            entity.HasIndex(x => new { x.VoucherId, x.MaterialInFinishedGoodId });
            entity.HasIndex(x => new { x.VoucherId, x.MaterialInConsumptionId });
            entity.HasIndex(x => new { x.VoucherId, x.InventoryInwardLineId });
            entity.HasIndex(x => new { x.VoucherId, x.PurchaseReturnLineId });
            entity.HasIndex(x => new { x.InventoryPostingId, x.MovementLineOrder }).IsUnique();
        });

        modelBuilder.Entity<VoucherLink>(entity =>
        {
            entity.ToTable("voucher_links");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.CompanyId).HasColumnName("company_id");
            entity.Property(x => x.SourceVoucherId).HasColumnName("source_voucher_id");
            entity.Property(x => x.TargetVoucherId).HasColumnName("target_voucher_id");
            entity.Property(x => x.LinkType).HasMaxLength(60).HasColumnName("link_type");
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(x => x.CreatedBy).HasMaxLength(100).HasColumnName("created_by");
            entity.HasOne(x => x.SourceVoucher).WithMany().HasForeignKey(x => x.SourceVoucherId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.TargetVoucher).WithMany().HasForeignKey(x => x.TargetVoucherId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.CompanyId, x.SourceVoucherId, x.TargetVoucherId, x.LinkType }).IsUnique();
            entity.HasIndex(x => new { x.CompanyId, x.TargetVoucherId });
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("audit_logs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.CompanyId).HasColumnName("company_id");
            entity.Property(x => x.EntityType).HasMaxLength(100).HasColumnName("entity_type");
            entity.Property(x => x.EntityId).HasColumnName("entity_id");
            entity.Property(x => x.Action).HasMaxLength(50).HasColumnName("action");
            entity.Property(x => x.Success).HasColumnName("success");
            entity.Property(x => x.Description).HasMaxLength(1000).HasColumnName("description");
            entity.Property(x => x.PerformedBy).HasMaxLength(100).HasColumnName("performed_by");
            entity.Property(x => x.PerformedAtUtc).HasColumnName("performed_at_utc");
            entity.Property(x => x.InventoryPostingId).HasColumnName("inventory_posting_id");
            entity.Property(x => x.CorrelationId).HasColumnName("correlation_id");
            entity.HasOne(x => x.InventoryPosting).WithMany().HasForeignKey(x => x.InventoryPostingId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.CompanyId, x.PerformedAtUtc });
            entity.HasIndex(x => x.InventoryPostingId);
            entity.HasIndex(x => x.CorrelationId);
        });

        modelBuilder.Entity<VoucherAuditRevision>(entity =>
        {
            entity.ToTable("voucher_audit_revisions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.VoucherAuditIdentity).HasColumnName("voucher_audit_identity");
            entity.Property(x => x.VoucherId).HasColumnName("voucher_id");
            entity.Property(x => x.CompanyId).HasColumnName("company_id");
            entity.Property(x => x.CompanyName).HasMaxLength(300).HasColumnName("company_name");
            entity.Property(x => x.FinancialYearId).HasColumnName("financial_year_id");
            entity.Property(x => x.FinancialYearName).HasMaxLength(100).HasColumnName("financial_year_name");
            entity.Property(x => x.VoucherTypeId).HasColumnName("voucher_type_id");
            entity.Property(x => x.VoucherTypeName).HasMaxLength(200).HasColumnName("voucher_type_name");
            entity.Property(x => x.VoucherTypeCode).HasMaxLength(100).HasColumnName("voucher_type_code");
            entity.Property(x => x.VoucherNumber).HasMaxLength(100).HasColumnName("voucher_number");
            entity.Property(x => x.RevisionNumber).HasColumnName("revision_number");
            entity.Property(x => x.SnapshotSchemaVersion).HasColumnName("snapshot_schema_version");
            entity.Property(x => x.Action).HasMaxLength(50).HasColumnName("action");
            entity.Property(x => x.Reason).HasMaxLength(1000).HasColumnName("reason");
            entity.Property(x => x.SnapshotJson).HasColumnType("text").HasColumnName("snapshot_json");
            entity.Property(x => x.ChangesJson).HasColumnType("text").HasColumnName("changes_json");
            entity.Property(x => x.ContentHash).HasMaxLength(64).HasColumnName("content_hash");
            entity.Property(x => x.PreviousChainHash).HasMaxLength(64).HasColumnName("previous_chain_hash");
            entity.Property(x => x.ChainHash).HasMaxLength(64).HasColumnName("chain_hash");
            entity.Property(x => x.RecordedAtUtc).HasColumnName("recorded_at_utc");
            entity.Property(x => x.RecordedBy).HasMaxLength(200).HasColumnName("recorded_by");
            entity.HasIndex(x => new { x.VoucherAuditIdentity, x.RevisionNumber }).IsUnique();
            entity.HasIndex(x => new { x.CompanyId, x.RecordedAtUtc });
            entity.HasIndex(x => new { x.CompanyId, x.VoucherId });
            entity.HasIndex(x => x.ChainHash).IsUnique();
        });
    }

    private static void ConfigureTallyExchange(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TallyCompanyLink>(entity =>
        {
            entity.ToTable("tally_company_links");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.CompanyId).HasColumnName("company_id");
            entity.Property(x => x.TallyCompanyName).HasMaxLength(300).HasColumnName("tally_company_name");
            entity.Property(x => x.TallyCompanyIdentity).HasMaxLength(100).HasColumnName("tally_company_identity");
            entity.Property(x => x.IsConfirmed).HasColumnName("is_confirmed");
            ConfigureAuditProperties(entity);
            entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.CompanyId, x.TallyCompanyIdentity }).IsUnique();
        });

        modelBuilder.Entity<TallyExchangeBatch>(entity =>
        {
            entity.ToTable("tally_exchange_batches");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.CompanyId).HasColumnName("company_id");
            entity.Property(x => x.TallyCompanyLinkId).HasColumnName("tally_company_link_id");
            entity.Property(x => x.Direction).HasMaxLength(20).HasColumnName("direction");
            entity.Property(x => x.FileName).HasMaxLength(300).HasColumnName("file_name");
            entity.Property(x => x.FileHash).HasMaxLength(64).HasColumnName("file_hash");
            entity.Property(x => x.Status).HasMaxLength(30).HasColumnName("status");
            entity.Property(x => x.NewMasterCount).HasColumnName("new_master_count");
            entity.Property(x => x.NewVoucherCount).HasColumnName("new_voucher_count");
            entity.Property(x => x.UpdatedVoucherCount).HasColumnName("updated_voucher_count");
            entity.Property(x => x.UnchangedVoucherCount).HasColumnName("unchanged_voucher_count");
            entity.Property(x => x.CancelledVoucherCount).HasColumnName("cancelled_voucher_count");
            entity.Property(x => x.ExceptionCount).HasColumnName("exception_count");
            entity.Property(x => x.Summary).HasColumnType("text").HasColumnName("summary");
            ConfigureAuditProperties(entity);
            entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.TallyCompanyLink).WithMany().HasForeignKey(x => x.TallyCompanyLinkId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.CompanyId, x.CreatedAtUtc });
        });

        modelBuilder.Entity<TallySyncRecord>(entity =>
        {
            entity.ToTable("tally_sync_records");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.CompanyId).HasColumnName("company_id");
            entity.Property(x => x.TallyCompanyLinkId).HasColumnName("tally_company_link_id");
            entity.Property(x => x.VoucherId).HasColumnName("voucher_id");
            entity.Property(x => x.TallyGuid).HasMaxLength(160).HasColumnName("tally_guid");
            entity.Property(x => x.TallyRemoteId).HasMaxLength(200).HasColumnName("tally_remote_id");
            entity.Property(x => x.VoucherTypeName).HasMaxLength(100).HasColumnName("voucher_type_name");
            entity.Property(x => x.VoucherNumber).HasMaxLength(100).HasColumnName("voucher_number");
            entity.Property(x => x.SourceHash).HasMaxLength(64).HasColumnName("source_hash");
            entity.Property(x => x.SyncState).HasMaxLength(40).HasColumnName("sync_state");
            entity.Property(x => x.LastImportedAtUtc).HasColumnName("last_imported_at_utc");
            entity.Property(x => x.LastExportedAtUtc).HasColumnName("last_exported_at_utc");
            ConfigureAuditProperties(entity);
            entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.TallyCompanyLink).WithMany().HasForeignKey(x => x.TallyCompanyLinkId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Voucher).WithMany().HasForeignKey(x => x.VoucherId).OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(x => new { x.TallyCompanyLinkId, x.TallyGuid }).IsUnique();
            entity.HasIndex(x => new { x.CompanyId, x.VoucherId });
        });

        modelBuilder.Entity<TallyImportException>(entity =>
        {
            entity.ToTable("tally_import_exceptions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.CompanyId).HasColumnName("company_id");
            entity.Property(x => x.ExchangeBatchId).HasColumnName("exchange_batch_id");
            entity.Property(x => x.TallyGuid).HasMaxLength(160).HasColumnName("tally_guid");
            entity.Property(x => x.VoucherTypeName).HasMaxLength(100).HasColumnName("voucher_type_name");
            entity.Property(x => x.VoucherNumber).HasMaxLength(100).HasColumnName("voucher_number");
            entity.Property(x => x.OrderNumber).HasMaxLength(100).HasColumnName("order_number");
            entity.Property(x => x.ReasonCode).HasMaxLength(60).HasColumnName("reason_code");
            entity.Property(x => x.Message).HasMaxLength(1000).HasColumnName("message");
            entity.Property(x => x.PayloadXml).HasColumnType("text").HasColumnName("payload_xml");
            entity.Property(x => x.Status).HasMaxLength(30).HasColumnName("status");
            ConfigureAuditProperties(entity);
            entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ExchangeBatch).WithMany().HasForeignKey(x => x.ExchangeBatchId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.CompanyId, x.Status, x.CreatedAtUtc });
        });

        modelBuilder.Entity<TallyVariantAllocationTask>(entity =>
        {
            entity.ToTable("tally_variant_allocation_tasks");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.CompanyId).HasColumnName("company_id");
            entity.Property(x => x.ExchangeBatchId).HasColumnName("exchange_batch_id");
            entity.Property(x => x.TallyGuid).HasMaxLength(160).HasColumnName("tally_guid");
            entity.Property(x => x.VoucherTypeName).HasMaxLength(100).HasColumnName("voucher_type_name");
            entity.Property(x => x.VoucherNumber).HasMaxLength(100).HasColumnName("voucher_number");
            entity.Property(x => x.StockItemName).HasMaxLength(300).HasColumnName("stock_item_name");
            entity.Property(x => x.Quantity).HasPrecision(19, 4).HasColumnName("quantity");
            entity.Property(x => x.UqcName).HasMaxLength(40).HasColumnName("uqc_name");
            entity.Property(x => x.PayloadXml).HasColumnType("text").HasColumnName("payload_xml");
            entity.Property(x => x.Status).HasMaxLength(30).HasColumnName("status");
            ConfigureAuditProperties(entity);
            entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ExchangeBatch).WithMany().HasForeignKey(x => x.ExchangeBatchId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.CompanyId, x.Status, x.CreatedAtUtc });
        });
    }

    private static void ConfigureBom(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BillOfMaterial>(entity =>
        {
            entity.ToTable("bill_of_materials");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.CompanyId).HasColumnName("company_id");
            entity.Property(x => x.StockItemId).HasColumnName("stock_item_id");
            entity.Property(x => x.Name).HasMaxLength(200).HasColumnName("name");
            entity.Property(x => x.NameNormalized).HasMaxLength(200).HasColumnName("name_normalized");
            entity.Property(x => x.OutputQuantity).HasPrecision(19, 4).HasColumnName("output_quantity");
            entity.Property(x => x.VersionNumber).HasColumnName("version_number");
            entity.Property(x => x.IsDefault).HasColumnName("is_default");
            entity.Property(x => x.IsActive).HasColumnName("is_active");
            entity.Property(x => x.CurrentRevisionId).HasColumnName("current_revision_id");
            ConfigureAuditProperties(entity);
            entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.StockItem).WithMany().HasForeignKey(x => x.StockItemId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.CompanyId, x.StockItemId, x.NameNormalized }).IsUnique();
            entity.HasIndex(x => new { x.CompanyId, x.StockItemId, x.IsActive });
            entity.HasOne(x => x.CurrentRevision).WithMany().HasForeignKey(x => x.CurrentRevisionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BillOfMaterialRevision>(entity =>
        {
            entity.ToTable("bill_of_material_revisions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.BomId).HasColumnName("bom_id");
            entity.Property(x => x.RevisionNumber).HasColumnName("revision_number");
            entity.Property(x => x.StockItemId).HasColumnName("stock_item_id");
            entity.Property(x => x.Name).HasMaxLength(200).HasColumnName("name");
            entity.Property(x => x.OutputQuantity).HasPrecision(19, 4).HasColumnName("output_quantity");
            entity.Property(x => x.ContentHash).HasMaxLength(64).HasColumnName("content_hash");
            entity.Property(x => x.Status).HasMaxLength(30).HasColumnName("status");
            entity.Property(x => x.ChangeReason).HasMaxLength(500).HasColumnName("change_reason");
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(x => x.CreatedBy).HasMaxLength(200).HasColumnName("created_by");
            entity.HasOne(x => x.Bom).WithMany(x => x.Revisions).HasForeignKey(x => x.BomId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.StockItem).WithMany().HasForeignKey(x => x.StockItemId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.BomId, x.RevisionNumber }).IsUnique();
            entity.HasIndex(x => new { x.BomId, x.ContentHash });
        });

        modelBuilder.Entity<BillOfMaterialRevisionLine>(entity =>
        {
            entity.ToTable("bill_of_material_revision_lines");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.BomRevisionId).HasColumnName("bom_revision_id");
            entity.Property(x => x.LineNumber).HasColumnName("line_number");
            entity.Property(x => x.ComponentStockItemId).HasColumnName("component_stock_item_id");
            entity.Property(x => x.ComponentVariantId).HasColumnName("component_variant_id");
            entity.Property(x => x.UqcId).HasColumnName("uqc_id");
            entity.Property(x => x.RequiredQuantity).HasPrecision(19, 4).HasColumnName("required_quantity");
            entity.Property(x => x.ChildBomId).HasColumnName("child_bom_id");
            entity.Property(x => x.ChildBomRevisionId).HasColumnName("child_bom_revision_id");
            entity.Property(x => x.ProcessId).HasColumnName("process_id");
            entity.Property(x => x.Notes).HasMaxLength(500).HasColumnName("notes");
            entity.HasOne(x => x.BomRevision).WithMany(x => x.Lines).HasForeignKey(x => x.BomRevisionId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.ComponentStockItem).WithMany().HasForeignKey(x => x.ComponentStockItemId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ComponentVariant).WithMany().HasForeignKey(x => x.ComponentVariantId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Uqc).WithMany().HasForeignKey(x => x.UqcId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ChildBom).WithMany().HasForeignKey(x => x.ChildBomId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ChildBomRevision).WithMany().HasForeignKey(x => x.ChildBomRevisionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Process).WithMany().HasForeignKey(x => x.ProcessId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.BomRevisionId, x.LineNumber }).IsUnique();
        });

        modelBuilder.Entity<BillOfMaterialLine>(entity =>
        {
            entity.ToTable("bill_of_material_lines");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.BomId).HasColumnName("bom_id");
            entity.Property(x => x.LineNumber).HasColumnName("line_number");
            entity.Property(x => x.ComponentStockItemId).HasColumnName("component_stock_item_id");
            entity.Property(x => x.ComponentVariantId).HasColumnName("component_variant_id");
            entity.Property(x => x.UqcId).HasColumnName("uqc_id");
            entity.Property(x => x.RequiredQuantity).HasPrecision(19, 4).HasColumnName("required_quantity");
            entity.Property(x => x.ChildBomId).HasColumnName("child_bom_id");
            entity.Property(x => x.ProcessId).HasColumnName("process_id");
            entity.Property(x => x.Notes).HasMaxLength(500).HasColumnName("notes");
            entity.HasOne(x => x.Bom).WithMany(x => x.Lines).HasForeignKey(x => x.BomId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.ComponentStockItem).WithMany().HasForeignKey(x => x.ComponentStockItemId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ComponentVariant).WithMany().HasForeignKey(x => x.ComponentVariantId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Uqc).WithMany().HasForeignKey(x => x.UqcId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ChildBom).WithMany().HasForeignKey(x => x.ChildBomId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Process).WithMany().HasForeignKey(x => x.ProcessId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.BomId, x.LineNumber }).IsUnique();
        });

        modelBuilder.Entity<JobWorkOrderBomStage>(entity =>
        {
            entity.ToTable("job_work_order_bom_stages");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.VoucherId).HasColumnName("voucher_id");
            entity.Property(x => x.FinishedGoodId).HasColumnName("finished_good_id");
            entity.Property(x => x.ParentStageId).HasColumnName("parent_stage_id");
            entity.Property(x => x.SourceBomId).HasColumnName("source_bom_id");
            entity.Property(x => x.SourceBomRevisionId).HasColumnName("source_bom_revision_id");
            entity.Property(x => x.SourceBomVersion).HasColumnName("source_bom_version");
            entity.Property(x => x.StableKey).HasColumnName("stable_key");
            entity.Property(x => x.StageNumber).HasColumnName("stage_number");
            entity.Property(x => x.StageLevel).HasColumnName("stage_level");
            entity.Property(x => x.StagePath).HasMaxLength(500).HasColumnName("stage_path");
            entity.Property(x => x.StageName).HasMaxLength(200).HasColumnName("stage_name");
            entity.Property(x => x.OutputStockItemId).HasColumnName("output_stock_item_id");
            entity.Property(x => x.OutputVariantId).HasColumnName("output_variant_id");
            entity.Property(x => x.OutputUqcId).HasColumnName("output_uqc_id");
            entity.Property(x => x.OutputQuantity).HasPrecision(19, 4).HasColumnName("output_quantity");
            entity.Property(x => x.ProcessId).HasColumnName("process_id");
            entity.Property(x => x.AssignedJobWorkerId).HasColumnName("assigned_job_worker_id");
            entity.Property(x => x.OutputGodownId).HasColumnName("output_godown_id");
            entity.Property(x => x.IsFinalStage).HasColumnName("is_final_stage");
            entity.HasOne(x => x.Voucher).WithMany().HasForeignKey(x => x.VoucherId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.FinishedGood).WithMany(x => x.BomStages).HasForeignKey(x => x.FinishedGoodId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.ParentStage).WithMany(x => x.ChildStages).HasForeignKey(x => x.ParentStageId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.SourceBom).WithMany().HasForeignKey(x => x.SourceBomId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.SourceBomRevision).WithMany().HasForeignKey(x => x.SourceBomRevisionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.OutputStockItem).WithMany().HasForeignKey(x => x.OutputStockItemId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.OutputVariant).WithMany().HasForeignKey(x => x.OutputVariantId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.OutputUqc).WithMany().HasForeignKey(x => x.OutputUqcId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Process).WithMany().HasForeignKey(x => x.ProcessId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.AssignedJobWorker).WithMany().HasForeignKey(x => x.AssignedJobWorkerId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.OutputGodown).WithMany().HasForeignKey(x => x.OutputGodownId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.FinishedGoodId, x.StagePath }).IsUnique();
            entity.HasIndex(x => new { x.VoucherId, x.StageNumber });
            entity.HasIndex(x => x.StableKey).IsUnique();
        });

        modelBuilder.Entity<JobWorkOrderRevision>(entity =>
        {
            entity.ToTable("job_work_order_revisions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.VoucherId).HasColumnName("voucher_id");
            entity.Property(x => x.RevisionNumber).HasColumnName("revision_number");
            entity.Property(x => x.ContentHash).HasMaxLength(64).HasColumnName("content_hash");
            entity.Property(x => x.SnapshotJson).HasColumnType("jsonb").HasColumnName("snapshot_json");
            entity.Property(x => x.ChangeReason).HasMaxLength(500).HasColumnName("change_reason");
            entity.Property(x => x.IsCurrent).HasColumnName("is_current");
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(x => x.CreatedBy).HasMaxLength(200).HasColumnName("created_by");
            entity.HasOne(x => x.Voucher).WithMany().HasForeignKey(x => x.VoucherId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.VoucherId, x.RevisionNumber }).IsUnique();
            entity.HasIndex(x => new { x.VoucherId, x.ContentHash });
        });

        modelBuilder.Entity<JobWorkOrderStageAssignment>(entity =>
        {
            entity.Property(x => x.ExpectedProcessRate).HasPrecision(19,4).HasColumnName("expected_process_rate");
            entity.ToTable("job_work_order_stage_assignments");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.BomStageId).HasColumnName("bom_stage_id");
            entity.Property(x => x.AssignmentVersion).HasColumnName("assignment_version");
            entity.Property(x => x.JobWorkerId).HasColumnName("job_worker_id");
            entity.Property(x => x.ExpectedCompletionDate).HasColumnName("expected_completion_date");
            entity.Property(x => x.Status).HasMaxLength(30).HasColumnName("status");
            entity.Property(x => x.Reason).HasMaxLength(500).HasColumnName("reason");
            entity.Property(x => x.ValidFromUtc).HasColumnName("valid_from_utc");
            entity.Property(x => x.ValidToUtc).HasColumnName("valid_to_utc");
            entity.Property(x => x.CreatedBy).HasMaxLength(200).HasColumnName("created_by");
            entity.HasOne(x => x.BomStage).WithMany(x => x.AssignmentHistory).HasForeignKey(x => x.BomStageId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.JobWorker).WithMany().HasForeignKey(x => x.JobWorkerId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.BomStageId, x.AssignmentVersion }).IsUnique();
        });
    }

    private static void ConfigureInventoryValuation(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InventoryPostingSequence>(entity =>
        {
            entity.ToTable("inventory_posting_sequences");
            entity.HasKey(x => x.CompanyId);
            entity.Property(x => x.CompanyId).HasColumnName("company_id");
            entity.Property(x => x.LastPostingOrder).HasColumnName("last_posting_order");
            entity.Property(x => x.ModifiedAtUtc).HasColumnName("modified_at_utc");
            entity.Property(x => x.ModifiedBy).HasMaxLength(100).HasColumnName("modified_by");
            entity.HasOne(x => x.Company).WithOne().HasForeignKey<InventoryPostingSequence>(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<InventoryPosting>(entity =>
        {
            entity.ToTable("inventory_postings");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.OperationId).HasColumnName("operation_id");
            entity.Property(x => x.CompanyId).HasColumnName("company_id");
            entity.Property(x => x.FinancialYearId).HasColumnName("financial_year_id");
            entity.Property(x => x.VoucherId).HasColumnName("voucher_id");
            entity.Property(x => x.EffectiveDate).HasColumnName("effective_date");
            entity.Property(x => x.EffectiveTime).HasColumnName("effective_time");
            entity.Property(x => x.PostingOrder).HasColumnName("posting_order");
            entity.Property(x => x.EventKind).HasMaxLength(20).HasColumnName("event_kind");
            entity.Property(x => x.ReversalOfPostingId).HasColumnName("reversal_of_posting_id");
            entity.Property(x => x.AlgorithmVersion).HasColumnName("algorithm_version");
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(x => x.CreatedBy).HasMaxLength(100).HasColumnName("created_by");
            entity.HasAlternateKey(x => x.OperationId);
            entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.FinancialYear).WithMany().HasForeignKey(x => x.FinancialYearId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Voucher).WithMany().HasForeignKey(x => x.VoucherId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ReversalOfPosting).WithMany().HasForeignKey(x => x.ReversalOfPostingId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.CompanyId, x.PostingOrder }).IsUnique();
            entity.HasIndex(x => x.ReversalOfPostingId).IsUnique();
            entity.HasIndex(x => new { x.CompanyId, x.EffectiveDate, x.PostingOrder });
            entity.HasIndex(x => new { x.VoucherId, x.PostingOrder });
        });

        modelBuilder.Entity<InventoryValuationRun>(entity =>
        {
            entity.ToTable("inventory_valuation_runs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key");
            entity.Property(x => x.CompanyId).HasColumnName("company_id");
            entity.Property(x => x.RunKind).HasMaxLength(30).HasColumnName("run_kind");
            entity.Property(x => x.AlgorithmVersion).HasColumnName("algorithm_version");
            entity.Property(x => x.SchemaVersion).HasColumnName("schema_version");
            entity.Property(x => x.RequestedScopeJson).HasColumnType("jsonb").HasColumnName("requested_scope_json");
            entity.Property(x => x.CutoffDate).HasColumnName("cutoff_date");
            entity.Property(x => x.State).HasMaxLength(20).HasColumnName("state");
            entity.Property(x => x.RequestedAtUtc).HasColumnName("requested_at_utc");
            entity.Property(x => x.RequestedBy).HasMaxLength(100).HasColumnName("requested_by");
            entity.Property(x => x.StartedAtUtc).HasColumnName("started_at_utc");
            entity.Property(x => x.StartedBy).HasMaxLength(100).HasColumnName("started_by");
            entity.Property(x => x.FinishedAtUtc).HasColumnName("finished_at_utc");
            entity.Property(x => x.FinishedBy).HasMaxLength(100).HasColumnName("finished_by");
            entity.Property(x => x.AttemptCount).HasColumnName("attempt_count");
            entity.Property(x => x.LeaseOwner).HasMaxLength(200).HasColumnName("lease_owner");
            entity.Property(x => x.LeaseExpiresAtUtc).HasColumnName("lease_expires_at_utc");
            entity.Property(x => x.HeartbeatAtUtc).HasColumnName("heartbeat_at_utc");
            entity.Property(x => x.EarliestAffectedDate).HasColumnName("earliest_affected_date");
            entity.Property(x => x.EarliestAffectedPostingOrder).HasColumnName("earliest_affected_posting_order");
            entity.Property(x => x.AffectedPositionCount).HasColumnName("affected_position_count");
            entity.Property(x => x.AffectedMovementCount).HasColumnName("affected_movement_count");
            entity.Property(x => x.AffectedLayerCount).HasColumnName("affected_layer_count");
            entity.Property(x => x.QuantityBefore).HasPrecision(19, 4).HasColumnName("quantity_before");
            entity.Property(x => x.QuantityAfter).HasPrecision(19, 4).HasColumnName("quantity_after");
            entity.Property(x => x.ValueBefore).HasPrecision(19, 4).HasColumnName("value_before");
            entity.Property(x => x.ValueAfter).HasPrecision(19, 4).HasColumnName("value_after");
            entity.Property(x => x.FailureSummary).HasMaxLength(1000).HasColumnName("failure_summary");
            entity.Property(x => x.DiagnosticReference).HasMaxLength(500).HasColumnName("diagnostic_reference");
            entity.Property(x => x.AuditCorrelationId).HasColumnName("audit_correlation_id");
            entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => x.IdempotencyKey).IsUnique();
            entity.HasIndex(x => new { x.CompanyId, x.State, x.RequestedAtUtc });
        });

        modelBuilder.Entity<InventoryValuationSetting>(entity =>
        {
            entity.ToTable("inventory_valuation_settings");
            entity.HasKey(x => x.CompanyId);
            entity.Property(x => x.CompanyId).HasColumnName("company_id");
            entity.Property(x => x.BookMethod).HasMaxLength(30).HasColumnName("book_method");
            entity.Property(x => x.LifecycleState).HasMaxLength(30).HasColumnName("lifecycle_state");
            entity.Property(x => x.CutoverDate).HasColumnName("cutover_date");
            entity.Property(x => x.CutoverRunId).HasColumnName("cutover_run_id");
            entity.Property(x => x.ActiveAlgorithmVersion).HasColumnName("active_algorithm_version");
            entity.Property(x => x.ActivatedAtUtc).HasColumnName("activated_at_utc");
            entity.Property(x => x.ActivatedBy).HasMaxLength(100).HasColumnName("activated_by");
            entity.Property(x => x.ActivationReason).HasMaxLength(1000).HasColumnName("activation_reason");
            entity.Property(x => x.LastSuccessfulReconciliationRunId).HasColumnName("last_successful_reconciliation_run_id");
            entity.Property(x => x.ModifiedAtUtc).HasColumnName("modified_at_utc");
            entity.Property(x => x.ModifiedBy).HasMaxLength(100).HasColumnName("modified_by");
            entity.Property(x => x.ConcurrencyToken).HasMaxLength(32).HasColumnName("concurrency_token").IsConcurrencyToken();
            entity.HasOne(x => x.Company).WithOne().HasForeignKey<InventoryValuationSetting>(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.CutoverRun).WithMany().HasForeignKey(x => x.CutoverRunId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.LastSuccessfulReconciliationRun).WithMany()
                .HasForeignKey(x => x.LastSuccessfulReconciliationRunId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<InventoryValuationPosition>(entity =>
        {
            entity.ToTable("inventory_valuation_positions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.CompanyId).HasColumnName("company_id");
            entity.Property(x => x.StockItemId).HasColumnName("stock_item_id");
            entity.Property(x => x.StockItemVariantId).HasColumnName("stock_item_variant_id");
            entity.Property(x => x.UqcId).HasColumnName("uqc_id");
            entity.Property(x => x.GodownId).HasColumnName("godown_id");
            entity.Property(x => x.State).HasMaxLength(30).HasColumnName("state");
            entity.Property(x => x.EarliestDirtyDate).HasColumnName("earliest_dirty_date");
            entity.Property(x => x.EarliestDirtyPostingOrder).HasColumnName("earliest_dirty_posting_order");
            entity.Property(x => x.CurrentSuccessfulRunId).HasColumnName("current_successful_run_id");
            entity.Property(x => x.LastErrorSummary).HasMaxLength(1000).HasColumnName("last_error_summary");
            entity.Property(x => x.ModifiedAtUtc).HasColumnName("modified_at_utc");
            entity.Property(x => x.ModifiedBy).HasMaxLength(100).HasColumnName("modified_by");
            entity.Property(x => x.ConcurrencyToken).HasMaxLength(32).HasColumnName("concurrency_token").IsConcurrencyToken();
            entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.StockItem).WithMany().HasForeignKey(x => x.StockItemId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.StockItemVariant).WithMany().HasForeignKey(x => x.StockItemVariantId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Uqc).WithMany().HasForeignKey(x => x.UqcId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Godown).WithMany().HasForeignKey(x => x.GodownId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.CurrentSuccessfulRun).WithMany().HasForeignKey(x => x.CurrentSuccessfulRunId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.CompanyId, x.State });
        });

        modelBuilder.Entity<InventoryCostLayer>(entity =>
        {
            entity.ToTable("inventory_cost_layers");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.CompanyId).HasColumnName("company_id");
            entity.Property(x => x.ReceiptMovementId).HasColumnName("receipt_movement_id");
            entity.Property(x => x.LayerSequence).HasColumnName("layer_sequence");
            entity.Property(x => x.StockItemId).HasColumnName("stock_item_id");
            entity.Property(x => x.StockItemVariantId).HasColumnName("stock_item_variant_id");
            entity.Property(x => x.UqcId).HasColumnName("uqc_id");
            entity.Property(x => x.GodownId).HasColumnName("godown_id");
            entity.Property(x => x.OriginKind).HasMaxLength(30).HasColumnName("origin_kind");
            entity.Property(x => x.SourceAllocationId).HasColumnName("source_allocation_id");
            entity.Property(x => x.OriginalQuantity).HasPrecision(19, 4).HasColumnName("original_quantity");
            entity.Property(x => x.OriginalValue).HasPrecision(19, 4).HasColumnName("original_value");
            entity.Property(x => x.UnitCost).HasPrecision(28, 8).HasColumnName("unit_cost");
            entity.Property(x => x.RemainingQuantity).HasPrecision(19, 4).HasColumnName("remaining_quantity");
            entity.Property(x => x.RemainingValue).HasPrecision(19, 4).HasColumnName("remaining_value");
            entity.Property(x => x.EffectiveDate).HasColumnName("effective_date");
            entity.Property(x => x.PostingOrder).HasColumnName("posting_order");
            entity.Property(x => x.MovementLineOrder).HasColumnName("movement_line_order");
            entity.Property(x => x.AlgorithmVersion).HasColumnName("algorithm_version");
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(x => x.CreatedBy).HasMaxLength(100).HasColumnName("created_by");
            entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ReceiptMovement).WithMany().HasForeignKey(x => x.ReceiptMovementId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.StockItem).WithMany().HasForeignKey(x => x.StockItemId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.StockItemVariant).WithMany().HasForeignKey(x => x.StockItemVariantId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Uqc).WithMany().HasForeignKey(x => x.UqcId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Godown).WithMany().HasForeignKey(x => x.GodownId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.SourceAllocation).WithOne().HasForeignKey<InventoryCostLayer>(x => x.SourceAllocationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.ReceiptMovementId, x.LayerSequence }).IsUnique();
            entity.HasIndex(x => x.SourceAllocationId).IsUnique();
        });

        modelBuilder.Entity<InventoryCostAllocation>(entity =>
        {
            entity.ToTable("inventory_cost_allocations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.OperationId).HasColumnName("operation_id");
            entity.Property(x => x.CompanyId).HasColumnName("company_id");
            entity.Property(x => x.OutwardMovementId).HasColumnName("outward_movement_id");
            entity.Property(x => x.SourceLayerId).HasColumnName("source_layer_id");
            entity.Property(x => x.AllocationSequence).HasColumnName("allocation_sequence");
            entity.Property(x => x.AllocatedQuantity).HasPrecision(19, 4).HasColumnName("allocated_quantity");
            entity.Property(x => x.AllocatedValue).HasPrecision(19, 4).HasColumnName("allocated_value");
            entity.Property(x => x.UnitCostSnapshot).HasPrecision(28, 8).HasColumnName("unit_cost_snapshot");
            entity.Property(x => x.ReversalOfAllocationId).HasColumnName("reversal_of_allocation_id");
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(x => x.CreatedBy).HasMaxLength(100).HasColumnName("created_by");
            entity.Property(x => x.AlgorithmVersion).HasColumnName("algorithm_version");
            entity.HasOne(x => x.Posting).WithMany().HasForeignKey(x => x.OperationId)
                .HasPrincipalKey(x => x.OperationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.OutwardMovement).WithMany().HasForeignKey(x => x.OutwardMovementId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.SourceLayer).WithMany().HasForeignKey(x => x.SourceLayerId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ReversalOfAllocation).WithMany().HasForeignKey(x => x.ReversalOfAllocationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.OutwardMovementId, x.AllocationSequence }).IsUnique();
            entity.HasIndex(x => new { x.OutwardMovementId, x.SourceLayerId }).IsUnique();
            entity.HasIndex(x => x.ReversalOfAllocationId).IsUnique();
            entity.HasIndex(x => new { x.SourceLayerId, x.Id });
        });
    }

    private static void ConfigureCompany(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Company> entity)
    {
        entity.ToTable("companies");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id");
        entity.Property(x => x.Name).HasMaxLength(200).HasColumnName("name");
        entity.Property(x => x.NameNormalized).HasMaxLength(200).HasColumnName("name_normalized");
        entity.Property(x => x.Code).HasMaxLength(50).HasColumnName("code");
        entity.Property(x => x.IsActive).HasColumnName("is_active");
        entity.Property(x => x.StockFrozenThrough).HasColumnName("stock_frozen_through");
        entity.Property(x => x.AllowNegativeStock).HasColumnName("allow_negative_stock").HasDefaultValue(false);
        ConfigureAuditProperties(entity);
        entity.HasIndex(x => x.NameNormalized).IsUnique();
        entity.HasIndex(x => x.Code).IsUnique();
    }

    private static void ConfigureFinancialYear(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<FinancialYear> entity)
    {
        entity.ToTable("financial_years");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id");
        entity.Property(x => x.CompanyId).HasColumnName("company_id");
        entity.Property(x => x.Name).HasMaxLength(50).HasColumnName("name");
        entity.Property(x => x.StartDate).HasColumnName("start_date");
        entity.Property(x => x.EndDate).HasColumnName("end_date");
        entity.Property(x => x.IsActive).HasColumnName("is_active");
        ConfigureAuditProperties(entity);
        entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        entity.HasIndex(x => new { x.CompanyId, x.Name }).IsUnique();
    }

    private static void ConfigureIdentity(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ApplicationUser>(entity =>
        {
            entity.ToTable("application_users");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.UserName).HasMaxLength(200).HasColumnName("user_name");
            entity.Property(x => x.UserNameNormalized).HasMaxLength(200).HasColumnName("user_name_normalized");
            entity.Property(x => x.DisplayName).HasMaxLength(200).HasColumnName("display_name");
            entity.Property(x => x.PasswordHash).HasMaxLength(500).HasColumnName("password_hash");
            entity.Property(x => x.SecurityStamp).HasMaxLength(32).HasColumnName("security_stamp");
            entity.Property(x => x.IsActive).HasColumnName("is_active");
            entity.Property(x => x.FailedLoginCount).HasColumnName("failed_login_count");
            entity.Property(x => x.LockoutEndUtc).HasColumnName("lockout_end_utc");
            entity.Property(x => x.LastLoginAtUtc).HasColumnName("last_login_at_utc");
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(x => x.ModifiedAtUtc).HasColumnName("modified_at_utc");
            entity.HasIndex(x => x.UserNameNormalized).IsUnique();
        });

        modelBuilder.Entity<SecurityRole>(entity =>
        {
            entity.ToTable("security_roles");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.Code).HasMaxLength(40).HasColumnName("code");
            entity.Property(x => x.Name).HasMaxLength(100).HasColumnName("name");
            entity.Property(x => x.IsSystem).HasColumnName("is_system");
            entity.HasIndex(x => x.Code).IsUnique();
        });

        modelBuilder.Entity<ApplicationUserRole>(entity =>
        {
            entity.ToTable("application_user_roles");
            entity.HasKey(x => new { x.UserId, x.RoleId });
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.RoleId).HasColumnName("role_id");
            entity.HasOne(x => x.User).WithMany(x => x.Roles).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Role).WithMany(x => x.Users).HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ApplicationUserCompany>(entity =>
        {
            entity.ToTable("application_user_companies");
            entity.HasKey(x => new { x.UserId, x.CompanyId });
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.CompanyId).HasColumnName("company_id");
            entity.Property(x => x.IsDefault).HasColumnName("is_default");
            entity.Property(x => x.IsActive).HasColumnName("is_active");
            entity.HasOne(x => x.User).WithMany(x => x.Companies).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.UserId, x.IsDefault });
            entity.HasIndex(x => new { x.CompanyId, x.IsActive });
        });
    }

    private static void ConfigureNamedMaster<T>(
        Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<T> entity,
        string tableName)
        where T : class, INamedMasterEntity
    {
        entity.ToTable(tableName);
        entity.HasKey(nameof(INamedMasterEntity.Id));
        entity.Property<long>(nameof(INamedMasterEntity.Id)).HasColumnName("id");
        entity.Property<long>(nameof(INamedMasterEntity.CompanyId)).HasColumnName("company_id");
        entity.Property<string>(nameof(INamedMasterEntity.Name)).HasMaxLength(200).HasColumnName("name");
        entity.Property<string>(nameof(INamedMasterEntity.NameNormalized)).HasMaxLength(200).HasColumnName("name_normalized");
        entity.Property<string>(nameof(INamedMasterEntity.Alias)).HasMaxLength(200).HasColumnName("alias");
        entity.Property<bool>(nameof(INamedMasterEntity.IsSystem)).HasColumnName("is_system");
        entity.Property<bool>(nameof(INamedMasterEntity.IsActive)).HasColumnName("is_active");
        ConfigureAuditProperties(entity);
        entity.HasOne<Company>(nameof(INamedMasterEntity.Company))
            .WithMany()
            .HasForeignKey(nameof(INamedMasterEntity.CompanyId))
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasIndex(nameof(INamedMasterEntity.CompanyId), nameof(INamedMasterEntity.NameNormalized)).IsUnique();
    }

    private static void ConfigureAuditProperties<T>(
        Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<T> entity)
        where T : class, IAuditableEntity
    {
        entity.Property<DateTimeOffset>(nameof(IAuditableEntity.CreatedAtUtc)).HasColumnName("created_at_utc");
        entity.Property<DateTimeOffset>(nameof(IAuditableEntity.ModifiedAtUtc)).HasColumnName("modified_at_utc");
        entity.Property<string>(nameof(IAuditableEntity.CreatedBy)).HasMaxLength(100).HasColumnName("created_by");
        entity.Property<string>(nameof(IAuditableEntity.ModifiedBy)).HasMaxLength(100).HasColumnName("modified_by");
        entity.Property<string>(nameof(IAuditableEntity.ConcurrencyToken)).HasMaxLength(32).HasColumnName("concurrency_token").IsConcurrencyToken();
    }
}
