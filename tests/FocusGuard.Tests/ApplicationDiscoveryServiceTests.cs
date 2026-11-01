using FocusGuard.Infrastructure.Windows;

namespace FocusGuard.Tests;

public sealed class ApplicationDiscoveryServiceTests
{
    [Theory]
    [InlineData("Discord", "C:\\Users\\Arion\\AppData\\Local\\Discord\\Discord.exe")]
    [InlineData("Obsidian", "C:\\Users\\Arion\\AppData\\Local\\Obsidian\\Obsidian.exe")]
    [InlineData("chrome", "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe")]
    public void IsUserFacingApplication_AllowsNormalDesktopApps(string processName, string path)
    {
        Assert.True(ApplicationDiscoveryService.IsUserFacingApplication(processName, path));
    }

    [Theory]
    [InlineData("NVIDIA Share", "C:\\Program Files\\NVIDIA Corporation\\NVIDIA Share\\NVIDIA Share.exe")]
    [InlineData("AMDRSServ", "C:\\Program Files\\AMD\\CNext\\CNext\\AMDRSServ.exe")]
    [InlineData("igfxEM", "C:\\Windows\\System32\\igfxEM.exe")]
    [InlineData("explorer", "C:\\Windows\\explorer.exe")]
    public void IsUserFacingApplication_RejectsSystemAndHardwareUtilities(string processName, string path)
    {
        Assert.False(ApplicationDiscoveryService.IsUserFacingApplication(processName, path));
    }

    [Fact]
    public void IsUserFacingApplication_RejectsProcessesWithoutAVisibleWindowTitle()
    {
        Assert.False(ApplicationDiscoveryService.IsUserFacingApplication(
            "GameInputRedistService",
            @"C:\Program Files\Microsoft GameInput\x64\GameInputRedistService.exe",
            string.Empty));
    }

    [Fact]
    public void IsLegacyBackgroundHelper_RecognizesOldPickerEntries()
    {
        Assert.True(ApplicationDiscoveryService.IsLegacyBackgroundHelper(new FocusGuard.Core.Models.ApplicationEntry
        {
            Name = "Microsoft Build Engine",
            ExecutablePath = @"C:\Program Files\Microsoft Visual Studio\MSBuild.exe"
        }));
    }
}
