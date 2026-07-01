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

#endif
