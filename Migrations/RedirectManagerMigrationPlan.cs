using Umbraco.Cms.Core.Packaging;
using Umbraco.Cms.Infrastructure.Migrations;
using Umbraco.RedirectManager.Models;

namespace Umbraco.RedirectManager.Migrations;

public class RedirectManagerPackageMigrationPlan : PackageMigrationPlan
{
    public RedirectManagerPackageMigrationPlan() : base("BT.RedirectManager")
    {
    }

    protected override void DefinePlan()
    {
        To<CreateRedirectManagerTable>(new Guid("C1686EA6-A8CF-4B7E-B91F-D4519EB17FDA"));
        To<AddIsRegexAndDescriptionColumns>(new Guid("EE2670E3-75C8-4BF6-8D70-36B10D5ECC65"));
        To<AddHitCountColumns>(new Guid("4F2A8B31-6C7C-4A8E-9E22-2D4D6D9CDDF1"));
        To<CreateMissedRequestsTable>(new Guid("7A1E9C42-3B5D-4F6A-8E11-9C2D5A7B3F04"));
        To<AddDomainColumn>(new Guid("B8D4E617-2F0A-4C9B-A5D3-6E1F8C0A9B72"));
        To<CreateRedirectHitDailyTable>(new Guid("1D9F4E23-6A8B-4C1D-9E7A-3B5C8D2F4A61"));
        To<AddAbTestColumns>(new Guid("6E3A9C15-4B7D-4F2E-8A1C-9D6E5F0B3C82"));
        To<AddPreserveQueryStringColumn>(new Guid("9C4F2A18-5E7B-4D3A-8F16-2C9E7B4A6D31"));
        To<AddValidityWindowColumns>(new Guid("A2E5F8C1-3B6D-4A9E-8F17-5C0D9E4B7A63"));
        To<AddAuditFieldColumns>(new Guid("D3B6A947-8F2C-4E15-9A03-6D7B1C5E9F82"));
        To<AddCultureColumn>(new Guid("E4F7A208-3C5B-46D2-9A81-7F0C3E6B4D95"));
        To<AddMissedRequestCategoryColumn>(new Guid("F5A8B319-4D6C-47E3-A092-8B1D4F7C5E06"));
    }
}

#if NET10_0_OR_GREATER

public class CreateRedirectManagerTable : AsyncMigrationBase
{
    public CreateRedirectManagerTable(IMigrationContext context) : base(context)
    {
    }

    protected override Task MigrateAsync()
    {
        if (TableExists(RedirectEntry.TableName) == false)
        {
            Create.Table<RedirectEntry>().Do();
        }

        return Task.CompletedTask;
    }
}

public class AddIsRegexAndDescriptionColumns : AsyncMigrationBase
{
    public AddIsRegexAndDescriptionColumns(IMigrationContext context) : base(context)
    {
    }

    protected override Task MigrateAsync()
    {
        if (TableExists(RedirectEntry.TableName) == false)
        {
            return Task.CompletedTask;
        }

        if (ColumnExists(RedirectEntry.TableName, "IsRegex") == false)
        {
            AddColumn<RedirectEntry>(RedirectEntry.TableName, "IsRegex");
        }

        if (ColumnExists(RedirectEntry.TableName, "Description") == false)
        {
            AddColumn<RedirectEntry>(RedirectEntry.TableName, "Description");
        }

        return Task.CompletedTask;
    }
}

public class AddHitCountColumns : AsyncMigrationBase
{
    public AddHitCountColumns(IMigrationContext context) : base(context)
    {
    }

    protected override Task MigrateAsync()
    {
        if (TableExists(RedirectEntry.TableName) == false)
        {
            return Task.CompletedTask;
        }

        if (ColumnExists(RedirectEntry.TableName, "HitCount") == false)
        {
            AddColumn<RedirectEntry>(RedirectEntry.TableName, "HitCount");
        }

        if (ColumnExists(RedirectEntry.TableName, "LastHitDate") == false)
        {
            AddColumn<RedirectEntry>(RedirectEntry.TableName, "LastHitDate");
        }

        return Task.CompletedTask;
    }
}

public class CreateMissedRequestsTable : AsyncMigrationBase
{
    public CreateMissedRequestsTable(IMigrationContext context) : base(context)
    {
    }

    protected override Task MigrateAsync()
    {
        if (TableExists(MissedRequest.TableName) == false)
        {
            Create.Table<MissedRequest>().Do();
        }

        return Task.CompletedTask;
    }
}

public class AddDomainColumn : AsyncMigrationBase
{
    public AddDomainColumn(IMigrationContext context) : base(context)
    {
    }

    protected override Task MigrateAsync()
    {
        if (TableExists(RedirectEntry.TableName) == false)
        {
            return Task.CompletedTask;
        }

        if (ColumnExists(RedirectEntry.TableName, "Domain") == false)
        {
            AddColumn<RedirectEntry>(RedirectEntry.TableName, "Domain");
        }

        return Task.CompletedTask;
    }
}

public class CreateRedirectHitDailyTable : AsyncMigrationBase
{
    public CreateRedirectHitDailyTable(IMigrationContext context) : base(context)
    {
    }

    protected override Task MigrateAsync()
    {
        if (TableExists(RedirectHitDaily.TableName) == false)
        {
            Create.Table<RedirectHitDaily>().Do();
        }

        return Task.CompletedTask;
    }
}

public class AddAbTestColumns : AsyncMigrationBase
{
    public AddAbTestColumns(IMigrationContext context) : base(context)
    {
    }

    protected override Task MigrateAsync()
    {
        if (TableExists(RedirectEntry.TableName) == false)
        {
            return Task.CompletedTask;
        }

        if (ColumnExists(RedirectEntry.TableName, "VariantBUrl") == false)
        {
            AddColumn<RedirectEntry>(RedirectEntry.TableName, "VariantBUrl");
        }

        if (ColumnExists(RedirectEntry.TableName, "VariantBWeight") == false)
        {
            AddColumn<RedirectEntry>(RedirectEntry.TableName, "VariantBWeight");
        }

        if (ColumnExists(RedirectEntry.TableName, "VariantBHitCount") == false)
        {
            AddColumn<RedirectEntry>(RedirectEntry.TableName, "VariantBHitCount");
        }

        if (ColumnExists(RedirectEntry.TableName, "VariantBLastHitDate") == false)
        {
            AddColumn<RedirectEntry>(RedirectEntry.TableName, "VariantBLastHitDate");
        }

        return Task.CompletedTask;
    }
}

public class AddPreserveQueryStringColumn : AsyncMigrationBase
{
    public AddPreserveQueryStringColumn(IMigrationContext context) : base(context)
    {
    }

    protected override Task MigrateAsync()
    {
        if (TableExists(RedirectEntry.TableName) == false)
        {
            return Task.CompletedTask;
        }

        if (ColumnExists(RedirectEntry.TableName, "PreserveQueryString") == false)
        {
            AddColumn<RedirectEntry>(RedirectEntry.TableName, "PreserveQueryString");
        }

        return Task.CompletedTask;
    }
}

public class AddValidityWindowColumns : AsyncMigrationBase
{
    public AddValidityWindowColumns(IMigrationContext context) : base(context)
    {
    }

    protected override Task MigrateAsync()
    {
        if (TableExists(RedirectEntry.TableName) == false)
        {
            return Task.CompletedTask;
        }

        if (ColumnExists(RedirectEntry.TableName, "ValidFrom") == false)
        {
            AddColumn<RedirectEntry>(RedirectEntry.TableName, "ValidFrom");
        }

        if (ColumnExists(RedirectEntry.TableName, "ValidUntil") == false)
        {
            AddColumn<RedirectEntry>(RedirectEntry.TableName, "ValidUntil");
        }

        return Task.CompletedTask;
    }
}

public class AddAuditFieldColumns : AsyncMigrationBase
{
    public AddAuditFieldColumns(IMigrationContext context) : base(context)
    {
    }

    protected override Task MigrateAsync()
    {
        if (TableExists(RedirectEntry.TableName) == false)
        {
            return Task.CompletedTask;
        }

        if (ColumnExists(RedirectEntry.TableName, "CreatedBy") == false)
        {
            AddColumn<RedirectEntry>(RedirectEntry.TableName, "CreatedBy");
        }

        if (ColumnExists(RedirectEntry.TableName, "ModifiedBy") == false)
        {
            AddColumn<RedirectEntry>(RedirectEntry.TableName, "ModifiedBy");
        }

        return Task.CompletedTask;
    }
}

public class AddCultureColumn : AsyncMigrationBase
{
    public AddCultureColumn(IMigrationContext context) : base(context)
    {
    }

    protected override Task MigrateAsync()
    {
        if (TableExists(RedirectEntry.TableName) == false)
        {
            return Task.CompletedTask;
        }

        if (ColumnExists(RedirectEntry.TableName, "Culture") == false)
        {
            AddColumn<RedirectEntry>(RedirectEntry.TableName, "Culture");
        }

        return Task.CompletedTask;
    }
}

public class AddMissedRequestCategoryColumn : AsyncMigrationBase
{
    public AddMissedRequestCategoryColumn(IMigrationContext context) : base(context)
    {
    }

    protected override async Task MigrateAsync()
    {
        if (TableExists(MissedRequest.TableName) == false)
        {
            return;
        }

        if (ColumnExists(MissedRequest.TableName, "Category") == false)
        {
            AddColumn<MissedRequest>(MissedRequest.TableName, "Category");
            await Database.ExecuteAsync(
                $"UPDATE {MissedRequest.TableName} SET Category = 'Unclassified' WHERE Category IS NULL");
        }
    }
}

#else

public class CreateRedirectManagerTable : MigrationBase
{
    public CreateRedirectManagerTable(IMigrationContext context) : base(context)
    {
    }

    protected override void Migrate()
    {
        if (TableExists(RedirectEntry.TableName) == false)
        {
            Create.Table<RedirectEntry>().Do();
        }
    }
}

public class AddIsRegexAndDescriptionColumns : MigrationBase
{
    public AddIsRegexAndDescriptionColumns(IMigrationContext context) : base(context)
    {
    }

    protected override void Migrate()
    {
        if (TableExists(RedirectEntry.TableName) == false)
        {
            return;
        }

        if (ColumnExists(RedirectEntry.TableName, "IsRegex") == false)
        {
            AddColumn<RedirectEntry>(RedirectEntry.TableName, "IsRegex");
        }

        if (ColumnExists(RedirectEntry.TableName, "Description") == false)
        {
            AddColumn<RedirectEntry>(RedirectEntry.TableName, "Description");
        }
    }
}

public class AddHitCountColumns : MigrationBase
{
    public AddHitCountColumns(IMigrationContext context) : base(context)
    {
    }

    protected override void Migrate()
    {
        if (TableExists(RedirectEntry.TableName) == false)
        {
            return;
        }

        if (ColumnExists(RedirectEntry.TableName, "HitCount") == false)
        {
            AddColumn<RedirectEntry>(RedirectEntry.TableName, "HitCount");
        }

        if (ColumnExists(RedirectEntry.TableName, "LastHitDate") == false)
        {
            AddColumn<RedirectEntry>(RedirectEntry.TableName, "LastHitDate");
        }
    }
}

public class CreateMissedRequestsTable : MigrationBase
{
    public CreateMissedRequestsTable(IMigrationContext context) : base(context)
    {
    }

    protected override void Migrate()
    {
        if (TableExists(MissedRequest.TableName) == false)
        {
            Create.Table<MissedRequest>().Do();
        }
    }
}

public class AddDomainColumn : MigrationBase
{
    public AddDomainColumn(IMigrationContext context) : base(context)
    {
    }

    protected override void Migrate()
    {
        if (TableExists(RedirectEntry.TableName) == false)
        {
            return;
        }

        if (ColumnExists(RedirectEntry.TableName, "Domain") == false)
        {
            AddColumn<RedirectEntry>(RedirectEntry.TableName, "Domain");
        }
    }
}

public class CreateRedirectHitDailyTable : MigrationBase
{
    public CreateRedirectHitDailyTable(IMigrationContext context) : base(context)
    {
    }

    protected override void Migrate()
    {
        if (TableExists(RedirectHitDaily.TableName) == false)
        {
            Create.Table<RedirectHitDaily>().Do();
        }
    }
}

public class AddAbTestColumns : MigrationBase
{
    public AddAbTestColumns(IMigrationContext context) : base(context)
    {
    }

    protected override void Migrate()
    {
        if (TableExists(RedirectEntry.TableName) == false)
        {
            return;
        }

        if (ColumnExists(RedirectEntry.TableName, "VariantBUrl") == false)
        {
            AddColumn<RedirectEntry>(RedirectEntry.TableName, "VariantBUrl");
        }

        if (ColumnExists(RedirectEntry.TableName, "VariantBWeight") == false)
        {
            AddColumn<RedirectEntry>(RedirectEntry.TableName, "VariantBWeight");
        }

        if (ColumnExists(RedirectEntry.TableName, "VariantBHitCount") == false)
        {
            AddColumn<RedirectEntry>(RedirectEntry.TableName, "VariantBHitCount");
        }

        if (ColumnExists(RedirectEntry.TableName, "VariantBLastHitDate") == false)
        {
            AddColumn<RedirectEntry>(RedirectEntry.TableName, "VariantBLastHitDate");
        }
    }
}

public class AddPreserveQueryStringColumn : MigrationBase
{
    public AddPreserveQueryStringColumn(IMigrationContext context) : base(context)
    {
    }

    protected override void Migrate()
    {
        if (TableExists(RedirectEntry.TableName) == false)
        {
            return;
        }

        if (ColumnExists(RedirectEntry.TableName, "PreserveQueryString") == false)
        {
            AddColumn<RedirectEntry>(RedirectEntry.TableName, "PreserveQueryString");
        }
    }
}

public class AddValidityWindowColumns : MigrationBase
{
    public AddValidityWindowColumns(IMigrationContext context) : base(context)
    {
    }

    protected override void Migrate()
    {
        if (TableExists(RedirectEntry.TableName) == false)
        {
            return;
        }

        if (ColumnExists(RedirectEntry.TableName, "ValidFrom") == false)
        {
            AddColumn<RedirectEntry>(RedirectEntry.TableName, "ValidFrom");
        }

        if (ColumnExists(RedirectEntry.TableName, "ValidUntil") == false)
        {
            AddColumn<RedirectEntry>(RedirectEntry.TableName, "ValidUntil");
        }
    }
}

public class AddAuditFieldColumns : MigrationBase
{
    public AddAuditFieldColumns(IMigrationContext context) : base(context)
    {
    }

    protected override void Migrate()
    {
        if (TableExists(RedirectEntry.TableName) == false)
        {
            return;
        }

        if (ColumnExists(RedirectEntry.TableName, "CreatedBy") == false)
        {
            AddColumn<RedirectEntry>(RedirectEntry.TableName, "CreatedBy");
        }

        if (ColumnExists(RedirectEntry.TableName, "ModifiedBy") == false)
        {
            AddColumn<RedirectEntry>(RedirectEntry.TableName, "ModifiedBy");
        }
    }
}

public class AddCultureColumn : MigrationBase
{
    public AddCultureColumn(IMigrationContext context) : base(context)
    {
    }

    protected override void Migrate()
    {
        if (TableExists(RedirectEntry.TableName) == false)
        {
            return;
        }

        if (ColumnExists(RedirectEntry.TableName, "Culture") == false)
        {
            AddColumn<RedirectEntry>(RedirectEntry.TableName, "Culture");
        }
    }
}

public class AddMissedRequestCategoryColumn : MigrationBase
{
    public AddMissedRequestCategoryColumn(IMigrationContext context) : base(context)
    {
    }

    protected override void Migrate()
    {
        if (TableExists(MissedRequest.TableName) == false)
        {
            return;
        }

        if (ColumnExists(MissedRequest.TableName, "Category") == false)
        {
            AddColumn<MissedRequest>(MissedRequest.TableName, "Category");
            Database.Execute(
                $"UPDATE {MissedRequest.TableName} SET Category = 'Unclassified' WHERE Category IS NULL");
        }
    }
}

#endif
