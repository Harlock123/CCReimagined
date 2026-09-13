using CCReimagined.App.ViewModels;
using CCReimagined.Core.Profiles;
using CCReimagined.Core.Providers;

namespace CCReimagined.Core.Tests;

/// <summary>
/// Saved connections must survive a restart, and must never carry a password to disk. The
/// second of those is the one that needs pinning down: a raw connection string is a free-text
/// field, so a password can arrive there without anyone deciding to save one.
/// </summary>
public sealed class ProfileTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(),
        $"ccr-profiles-{Guid.NewGuid():N}",
        "connections.json");

    public void Dispose()
    {
        var directory = Path.GetDirectoryName(_path);

        if (directory is not null && Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    private ProfileStore NewStore() => new(_path);

    [Theory]
    [InlineData("Server=db;Database=app;User Id=sa;Password=hunter2", "hunter2")]
    [InlineData("Server=db;Uid=sa;Pwd=hunter2", "hunter2")]
    [InlineData("Host=db;Username=sa;PASSWORD=hunter2", "hunter2")]
    [InlineData("Server=db;Client Secret=hunter2", "hunter2")]
    public void Scrubbing_removes_every_spelling_of_a_secret(string raw, string secret)
    {
        var scrubbed = ConnectionProfile.ScrubConnectionString(raw);

        Assert.DoesNotContain(secret, scrubbed, StringComparison.OrdinalIgnoreCase);
        // The rest of the string has to survive, or the profile is useless.
        Assert.Contains("db", scrubbed, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Scrubbing_drops_a_connection_string_it_cannot_parse()
    {
        // Rather than persist something that might hide a password in an unknown shape.
        Assert.Equal("", ConnectionProfile.ScrubConnectionString("=not;;a=valid=string="));
    }

    [Fact]
    public void A_saved_profile_reaches_disk_without_its_password()
    {
        var store = NewStore();

        store.Save(new ProfileBook
        {
            Profiles =
            [
                new ConnectionProfile
                {
                    Name = "prod",
                    ProviderId = "sqlserver",
                    Host = "db.internal",
                    UseRawConnectionString = true,
                    RawConnectionString = "Server=db.internal;User Id=sa;Password=hunter2",
                },
            ],
            LastUsedProfileName = "prod",
        });

        var onDisk = File.ReadAllText(_path);

        Assert.DoesNotContain("hunter2", onDisk, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("db.internal", onDisk);

        var reloaded = store.Load();
        Assert.Equal("prod", reloaded.LastUsedProfileName);
        Assert.Equal("db.internal", Assert.Single(reloaded.Profiles).Host);
    }

    [Fact]
    public void A_missing_or_corrupt_file_loads_as_an_empty_book()
    {
        var store = NewStore();
        Assert.Empty(store.Load().Profiles);

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "{ this is not json");

        // Losing saved profiles is bad; refusing to start is worse.
        Assert.Empty(store.Load().Profiles);
    }

    [Fact]
    public void Saving_a_profile_captures_the_fields_but_not_the_password()
    {
        var vm = new MainViewModel(NewStore())
        {
            SelectedProvider = ProviderRegistry.ById("postgresql"),
        };

        vm.Host = "db.internal";
        vm.Port = "5433";
        vm.DatabaseOrPath = "billing";
        vm.UseIntegratedSecurity = false;
        vm.UserName = "reporting";
        vm.Password = "hunter2";
        vm.ProfileName = "Billing";

        vm.SaveProfileCommand.Execute(null);

        var saved = Assert.Single(vm.Profiles);
        Assert.Equal("Billing", saved.Name);
        Assert.Equal("postgresql", saved.ProviderId);
        Assert.Equal(5433, saved.Port);
        Assert.Equal("reporting", saved.UserName);

        Assert.DoesNotContain("hunter2", File.ReadAllText(_path), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Saving_under_an_existing_name_updates_rather_than_duplicates()
    {
        var vm = new MainViewModel(NewStore());

        vm.Host = "first";
        vm.ProfileName = "Shared";
        vm.SaveProfileCommand.Execute(null);

        vm.Host = "second";
        vm.ProfileName = "Shared";
        vm.SaveProfileCommand.Execute(null);

        Assert.Equal("second", Assert.Single(vm.Profiles).Host);
    }

    [Fact]
    public void Saving_under_a_new_name_is_how_save_as_works()
    {
        var vm = new MainViewModel(NewStore());

        vm.Host = "first";
        vm.ProfileName = "One";
        vm.SaveProfileCommand.Execute(null);

        vm.Host = "second";
        vm.ProfileName = "Two";
        vm.SaveProfileCommand.Execute(null);

        Assert.Equal(2, vm.Profiles.Count);
    }

    [Fact]
    public void An_unnamed_profile_is_refused_rather_than_saved_blank()
    {
        var vm = new MainViewModel(NewStore());

        vm.ProfileName = "   ";
        vm.SaveProfileCommand.Execute(null);

        Assert.Empty(vm.Profiles);
        Assert.True(vm.StatusIsError);
    }

    [Fact]
    public void The_last_used_profile_comes_back_on_the_next_launch()
    {
        var first = new MainViewModel(NewStore())
        {
            SelectedProvider = ProviderRegistry.ById("mysql"),
        };

        first.Host = "reports.internal";
        first.Port = "3307";
        first.DatabaseOrPath = "warehouse";
        first.UserName = "etl";
        first.ProfileName = "Warehouse";
        first.SaveProfileCommand.Execute(null);

        // A fresh view model over the same file stands in for restarting the app.
        var second = new MainViewModel(NewStore());

        Assert.Equal("Warehouse", second.SelectedProfile?.Name);
        Assert.Equal("reports.internal", second.Host);
        Assert.Equal("3307", second.Port);
        Assert.Equal("warehouse", second.DatabaseOrPath);
        Assert.Equal("etl", second.UserName);
        Assert.Equal("mysql", second.SelectedProvider.Id);

        // Nothing to restore a password from, by design.
        Assert.Equal("", second.Password);
    }

    [Fact]
    public void Selecting_a_profile_repoints_the_whole_connection_form()
    {
        var store = NewStore();
        var vm = new MainViewModel(store) { SelectedProvider = ProviderRegistry.ById("sqlite") };

        vm.DatabaseOrPath = "/tmp/one.db";
        vm.ProfileName = "Local SQLite";
        vm.SaveProfileCommand.Execute(null);

        vm.SelectedProvider = ProviderRegistry.ById("sqlserver");
        vm.Host = "elsewhere";
        vm.ProfileName = "Remote";
        vm.SaveProfileCommand.Execute(null);

        vm.SelectedProfile = vm.Profiles.First(p => p.Name == "Local SQLite");

        // Switching back must restore the provider *and* the fields the provider reset.
        Assert.Equal("sqlite", vm.SelectedProvider.Id);
        Assert.Equal("/tmp/one.db", vm.DatabaseOrPath);
        Assert.False(vm.NeedsHost);
    }

    [Fact]
    public void Deleting_a_profile_removes_it_from_disk_too()
    {
        var vm = new MainViewModel(NewStore());

        vm.Host = "gone";
        vm.ProfileName = "Doomed";
        vm.SaveProfileCommand.Execute(null);

        vm.SelectedProfile = vm.Profiles[0];
        vm.DeleteProfileCommand.Execute(null);

        Assert.Empty(vm.Profiles);
        Assert.Empty(new MainViewModel(NewStore()).Profiles);
    }

    [Fact]
    public void The_editor_theme_is_remembered_between_launches()
    {
        var vm = new MainViewModel(NewStore());
        var nord = vm.EditorThemes.First(t => t.Name == "Nord");

        vm.SelectedEditorTheme = nord;

        Assert.Equal("Nord", new MainViewModel(NewStore()).SelectedEditorTheme.Name);
    }
}
