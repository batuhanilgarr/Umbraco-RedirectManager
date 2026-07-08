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

#endif
