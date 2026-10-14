using SteamCmdHelper;
using Xunit;

namespace SteamCmdHelper.Tests;

public class SteamCmdOutputTests
{
    static readonly string[] Login =
    {
        "Redirecting stderr to 'C:\\steamcmd\\logs\\stderr.txt'",
        "Loading Steam API...OK",
        "Logging in using cached credentials.",
        "Logging in user 'modder' to Steam Public...OK",
        "Waiting for client config...OK",
        "Waiting for user info...OK",
    };

    static readonly string[] Upload =
    {
        "Preparing update...",
        "Preparing content...",
        "Uploading content...",
        "Uploading preview image...",
        "Committing update...",
    };

    static string[] Run(params string[] tail) => Login.Concat(Upload).Concat(tail).ToArray();

    const string VdfUpdate = "\"workshopitem\"\n{\n\t\"appid\" \"294100\"\n\t\"publishedfileid\" \"2001112223\"\n}";
    const string VdfCreateBefore = "\"workshopitem\"\n{\n\t\"appid\" \"294100\"\n\t\"publishedfileid\" \"0\"\n}";
    const string VdfCreateAfter = "\"workshopitem\"\n{\n\t\"appid\" \"294100\"\n\t\"publishedfileid\" \"3004445556\"\n}";

    [Fact]
    public void Update_with_success_line_and_exit_zero_succeeds()
    {
        var r = SteamCmdOutput.Evaluate(Run("Success."), 0, "2001112223", VdfUpdate);
        Assert.True(r.Ok, r.Reason);
        Assert.Equal("2001112223", r.PublishedFileId);
    }

    [Fact]
    public void Create_takes_new_id_from_build_file()
    {
        var r = SteamCmdOutput.Evaluate(Run("Success."), 0, null, VdfCreateAfter);
        Assert.True(r.Ok, r.Reason);
        Assert.Equal("3004445556", r.PublishedFileId);
    }

    [Fact]
    public void Create_falls_back_to_id_printed_by_steamcmd()
    {
        var r = SteamCmdOutput.Evaluate(Run("PublishFileID : 3004445556", "Success."), 0, null, VdfCreateBefore);
        Assert.True(r.Ok, r.Reason);
        Assert.Equal("3004445556", r.PublishedFileId);
    }

    [Fact]
    public void Create_without_returned_id_fails()
    {
        var r = SteamCmdOutput.Evaluate(Run("Success."), 0, null, VdfCreateBefore);
        Assert.False(r.Ok);
        Assert.Contains("Workshop id", r.Reason);
        Assert.Null(r.PublishedFileId);
    }

    [Fact]
    public void Create_with_conflicting_ids_fails()
    {
        var r = SteamCmdOutput.Evaluate(Run("PublishFileID : 3999999999", "Success."), 0, null, VdfCreateAfter);
        Assert.False(r.Ok);
    }

    [Fact]
    public void Missing_success_line_fails_even_with_exit_zero()
    {
        var r = SteamCmdOutput.Evaluate(Run(), 0, "2001112223", VdfUpdate);
        Assert.False(r.Ok);
        Assert.Contains("Success", r.Reason);
    }

    [Fact]
    public void Nonzero_exit_fails_even_with_success_line()
    {
        var r = SteamCmdOutput.Evaluate(Run("Success."), 6, "2001112223", VdfUpdate);
        Assert.False(r.Ok);
        Assert.Contains("code 6", r.Reason);
    }

    [Theory]
    [InlineData("ERROR! Failed to update workshop item (Access Denied).")]
    [InlineData("ERROR! Timeout uploading manifest (Failure)")]
    [InlineData("Failed to upload content (k_EResultLimitExceeded)")]
    public void SteamCmd_error_line_fails_even_with_success_and_exit_zero(string error)
    {
        var lines = Login.Concat(new[] { "Uploading content...", error, "Success." });
        var r = SteamCmdOutput.Evaluate(lines, 0, "2001112223", VdfUpdate);
        Assert.False(r.Ok);
        Assert.Contains(error, r.Reason);
    }

    [Fact]
    public void Failed_login_fails()
    {
        var lines = new[] { "Loading Steam API...OK", "Logging in user 'modder' to Steam Public...FAILED (Invalid Password)" };
        var r = SteamCmdOutput.Evaluate(lines, 0, "2001112223", VdfUpdate);
        Assert.False(r.Ok);
        Assert.Contains("Invalid Password", r.Reason);
    }

    [Fact]
    public void Failed_login_with_result_code_fails()
    {
        var r = SteamCmdOutput.Evaluate(new[] { "FAILED login with result code Two-factor code mismatch" }, 5, "2001112223", VdfUpdate);
        Assert.False(r.Ok);
    }

    [Fact]
    public void Harmless_lines_mentioning_error_or_failed_do_not_fail_the_upload()
    {
        // The old check flagged any line with the word ERROR or FAILED, so these turned a good upload into a failure.
        var r = SteamCmdOutput.Evaluate(Run(
            "Failed to init SDL priority manager: SDL not found",
            "Uploading Textures/UI/Error.png",
            "Success."), 0, "2001112223", VdfUpdate);
        Assert.True(r.Ok, r.Reason);
    }

    [Fact]
    public void Ansi_codes_and_padding_are_ignored()
    {
        var r = SteamCmdOutput.Evaluate(Run("\u001b[0m  Success.  "), 0, "2001112223", VdfUpdate);
        Assert.True(r.Ok, r.Reason);
    }

    [Fact]
    public void Update_reporting_another_items_id_fails()
    {
        var r = SteamCmdOutput.Evaluate(Run("PublishFileID : 3999999999", "Success."), 0, "2001112223", VdfUpdate);
        Assert.False(r.Ok);
        Assert.Contains("3999999999", r.Reason);
    }

    [Fact]
    public void Failed_create_still_reports_an_assigned_id()
    {
        // If Steam handed out an id before the run failed, the caller must save it so the item is not created twice.
        var r = SteamCmdOutput.Evaluate(Run("ERROR! Failed to update workshop item (Failure)."), 0, null, VdfCreateAfter);
        Assert.False(r.Ok);
        Assert.Equal("3004445556", r.PublishedFileId);
    }
}
