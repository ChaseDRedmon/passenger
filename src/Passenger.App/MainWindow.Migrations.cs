using Avalonia.Controls;
using Passenger.App.Migrations;
using A = Passenger.App.AppActions;

namespace Passenger.App;

/// <summary>The window's Migrations: its tool window over the folder's EF Core contexts (<see cref="MigrationsController"/>),
/// the commands it runs as the window's actions, and the questions they ask (a migration's name, a script's range, a gap's
/// fix before a command goes on).</summary>
public partial class MainWindow
{
    private MigrationsController? migrations;
    private MigrationsView? migrationsView;

    /// <summary>The Migrations window's model.</summary>
    public MigrationsController Migrations => migrations!;

    /// <summary>The Migrations tool window.</summary>
    public MigrationsView MigrationsView => migrationsView!;

    private void AddMigrations()
    {
        MigrationsController controller = migrations = new MigrationsController(new MigrationsParts(Editors, Bottom, Dock, work, Say, () => session, () => buildRun,
            ChooseAsync, (project, id, version) => NuGet.InstallAsync(project, id, version), (project, packages) => NuGet.InstallSetAsync(project, packages),
            () => NuGet.FeedAsync(null), FollowWritten, OpenFile, () => StateFolder, ShowPendingModelChanges)
        {
            AsksNoOne = () => AgentsOwn,
        });
        // The NuGet window's update of one of EF Core's packages is weighed against the project's provider first.
        NuGet.BeforeInstall = controller.WeighUpdateAsync;
        MigrationsView view = migrationsView = new MigrationsView(controller);
        view.ActionRequested += (_, action) => Request(action);
        controller.AddAsked += (_, _) => Request(A.AddMigration);
        Dock.Add(new Docking.ToolWindow(ToolWindowIds.Migrations, "Migrations", "Icon.Database", view, Docking.ToolAnchor.BottomLeft)
        {
            Action = A.ShowMigrations,
            Menus = [new MenuPlace("Tools", 0, 5)],
            Chosen = () => view.Chosen,
        });
        // Shown, it lists what it can at once: the commands it runs found ready the moment the window says what is missing.
        view.AttachedToVisualTree += (_, _) => UiThread.Post(ListMigrationsIfShown);
        controller.Changed += (_, _) => ListMigrationsIfShown();
        Func<ActionContext, string?> none = _ => Folder is null ? Refusals.NoFolder : controller.Projects.Count == 0 ? "No project here uses EF Core" : null;
        actions!.Add(new IdeAction(A.MigrationsRefresh, "Refresh Migrations", "Migrations", c => _ = controller.ListAsync(A.MigrationsRefresh))
        {
            WhyNot = none,
            Menus = [new MenuPlace("Tools/Migrations", 0, 0)],
            Words = ["dotnet ef migrations list", "Entity Framework Core", "EF Core"],
        });
        actions.Add(new IdeAction(A.AddMigration, "Add Migration…", "Migrations", c => _ = AddMigrationAsync())
        {
            WhyNot = none,
            Menus = [new MenuPlace("Tools/Migrations", 0, 1)],
            Words = ["dotnet ef migrations add", "Entity Framework Core", "EF Core"],
        });
        actions.Add(new IdeAction(A.RemoveLastMigration, "Remove Last Migration", "Migrations", c => _ = controller.RemoveLastAsync(A.RemoveLastMigration))
        {
            WhyNot = none,
            Menus = [new MenuPlace("Tools/Migrations", 0, 2)],
            Words = ["dotnet ef migrations remove"],
        });
        actions.Add(new IdeAction(A.UpdateDatabase, "Update Database", "Migrations", c => _ = controller.UpdateAsync(A.UpdateDatabase, null))
        {
            WhyNot = none,
            Menus = [new MenuPlace("Tools/Migrations", 1, 0)],
            Words = ["dotnet ef database update", "Apply Migrations"],
        });
        actions.Add(new IdeAction(A.UpdateDatabaseTo, "Update Database to Chosen Migration", "Migrations", c =>
        {
            if (view.Chosen is { } chosen)
            {
                _ = controller.UpdateAsync(A.UpdateDatabaseTo, chosen.Id);
            }
        })
        {
            WhyNot = context => none(context) ?? (view.Chosen is null ? "No migration is chosen in the Migrations window" : null),
            TitleIn = _ => view.Chosen is { } chosen ? $"Update Database to {chosen.Name}" : "Update Database to Chosen Migration",
            Menus = [new MenuPlace("Tools/Migrations", 1, 1)],
            Words = ["Roll Back Migration", "dotnet ef database update"],
        });
        actions.Add(new IdeAction(A.ScriptMigrations, "Script Migrations…", "Migrations", c => _ = ScriptMigrationsAsync())
        {
            WhyNot = none,
            Menus = [new MenuPlace("Tools/Migrations", 2, 0)],
            Words = ["dotnet ef migrations script", "SQL Script"],
        });
        actions.Add(new IdeAction(A.BundleMigrations, "Bundle Migrations", "Migrations", c => _ = controller.BundleAsync(A.BundleMigrations))
        {
            WhyNot = none,
            Menus = [new MenuPlace("Tools/Migrations", 2, 1)],
            Words = ["dotnet ef migrations bundle", "efbundle"],
        });
        actions.Add(new IdeAction(A.MigrationsFix, "Fix What Migrations Lack", "Migrations", c =>
        {
            if (FirstFix() is { } fix)
            {
                _ = controller.FixAsync(fix);
            }
        })
        {
            WhyNot = context => none(context) ?? (FirstFix() is null ? "The Migrations window shows nothing to fix" : null),
            TitleIn = _ => FirstFix()?.Title ?? "Fix What Migrations Lack",
            Words = ["dotnet tool install dotnet-ef", "Microsoft.EntityFrameworkCore.Design"],
        });
        ActionButtons.ShowMarked(view, actions);

        EfFix? FirstFix() => controller.Gaps.Concat(controller.Failure is { } failed ? [failed] : []).SelectMany(g => g.Fixes).FirstOrDefault();
    }

    /// <summary>The session's projects as the Migrations window follows them, from the session's first moment.</summary>
    private void BindMigrations(ProjectSession next)
    {
        migrations?.Bind(next);
        next.WorkspaceChanged += (_, _) => FollowMigrations(next);
        next.ProjectsEvaluated += (_, _) => FollowMigrations(next);
        buildRun.BuildFinished += (_, built) =>
        {
            FollowMigrations(next);
            if (session == next)
            {
                migrations?.AfterBuild(built);
            }
        };
    }

    /// <summary>The contexts whose model has changes no migration covers, in Problems' Build tab beside the build's own, each
    /// at its class, where Show Context Actions offers Add Migration… (<see cref="MigrationFixes"/>).</summary>
    private void ShowPendingModelChanges(IReadOnlyList<Run.Problem> pending)
    {
        session.Problems.RemoveAll(p => p.Code == MigrationsController.PendingCode);
        session.Problems.AddRange(pending);
        buildRun.ShowProblems();
    }

    /// <summary>Add Migration… on the line of a context whose model has changes no migration covers: that context chosen in the
    /// Migrations window and the migration's name asked.</summary>
    private IReadOnlyList<CSharp.EditorFix> MigrationFixes(Workspace.OpenDocument file, int offset)
    {
        if (migrations is not { Pending.Count: > 0 } controller)
        {
            return [];
        }
        int line = file.Text.Snapshot.GetPosition(offset).Line;
        return [.. controller.Pending.Where(p => p.File is { } at && Core.PathNames.Same(at, file.Path) && p.Position.Line == line)
            .Select(p => new CSharp.EditorFix("Add Migration…", $"for {p.Context}'s model changes", new Text.TextRange(offset, 0), string.Empty)
            {
                Elsewhere = (_, _) =>
                {
                    controller.ChooseContext(p.Context);
                    // Not awaited: its name is asked of the person, which no fix's own work waits on.
                    _ = AddMigrationAsync();
                    return Task.FromResult(new CSharp.RefactorOutcome($"Add Migration: a name for {p.Context}'s migration", null, [], []));
                },
            })];
    }

    private void FollowMigrations(ProjectSession next)
    {
        if (session == next && migrations is { Busy: null } shown)
        {
            shown.Follow();
        }
    }

    /// <summary>The Migrations window, shown, lists its context's migrations by itself once for the choices as they stand,
    /// as soon as the projects are read and nothing it shows stops dotnet ef: what is missing is said first, never asked.</summary>
    private void ListMigrationsIfShown()
    {
        if (migrations is { WantsListing: true } controller && Dock.IsShown(ToolWindowIds.Migrations) && !IsBuilding)
        {
            controller.ListingTried();
            UiThread.Post(() => Request(A.MigrationsRefresh));
        }
    }

    /// <summary>Add Migration…: its name asked, then added to the chosen context.</summary>
    private async Task AddMigrationAsync()
    {
        TextBox box = new() { Name = "MigrationName", PlaceholderText = "AddCustomerEmail" };
        ConfirmDialog dialog = new("Add Migration", $"A name for the migration of {migrations!.Context ?? migrations.Project?.Name}, as a C# class is named.", "Add", detail: box);
        dialog.Opened += (_, _) => box.Focus();
        if (!await dialog.ShowDialog<bool>(this) || box.Text?.Trim() is not { Length: > 0 } name)
        {
            return;
        }
        if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[\p{L}_][\p{L}\p{Nd}_]*$"))
        {
            status.Text = $"{name} is no C# name: a migration is a class of that name";
            return;
        }
        await migrations.AddAsync(A.AddMigration, name);
    }

    /// <summary>Script Migrations…: its range and idempotence asked, then scripted into an editor tab.</summary>
    private async Task ScriptMigrationsAsync()
    {
        MigrationsController controller = migrations!;
        if (!controller.Listed && !(await controller.ListAsync(A.MigrationsRefresh)).Done)
        {
            return;
        }
        MigrationScriptDialog dialog = new(controller.Migrations, controller.Context ?? controller.Project?.Name ?? "the context");
        if (await dialog.ShowDialog<MigrationScript?>(this) is not { } asked)
        {
            return;
        }
        EfDone done = await controller.ScriptAsync(A.ScriptMigrations, asked.From, asked.To, asked.Idempotent);
        if (!done.Done && controller.Failure?.Fixes.Any(f => f.Kind == EfFixKind.ScriptPlain) == true
            && await ChooseAsync("Script Migrations", $"{controller.Failure.Said}. Script them without idempotence?", ["Script Without Idempotence"], "Script Without Idempotence") is not null)
        {
            await controller.ScriptAsync(A.ScriptMigrations, asked.From, asked.To, idempotent: false);
        }
    }

    /// <summary>Asks which of several ways to go on (<see cref="ChoiceDialog"/>): the words of the one chosen, or null.</summary>
    private Task<string?> ChooseAsync(string title, string text, IReadOnlyList<string> choices, string? chosen) =>
        new ChoiceDialog(title, text, choices, chosen).ShowDialog<string?>(this);
}
