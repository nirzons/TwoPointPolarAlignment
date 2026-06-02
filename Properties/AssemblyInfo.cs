using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// [MANDATORY] The following GUID is used as a unique identifier of the plugin. Generate a fresh one for your plugin!
[assembly: Guid("0e9e3e58-42fc-4553-8e6e-aba061af4f54")]

// [MANDATORY] The assembly versioning
//Should be incremented for each new release build of a plugin
[assembly: AssemblyVersion("1.3.1.0")]
[assembly: AssemblyFileVersion("1.3.1.0")]

// [MANDATORY] The name of your plugin
[assembly: AssemblyTitle("2-Point Polar Alignment")]
// [MANDATORY] A short description of your plugin
[assembly: AssemblyDescription("Fast polar alignment using home position and a 90° RA rotation")]

// The following attributes are not required for the plugin per se, but are required by the official manifest meta data

// Your name
[assembly: AssemblyCompany("Nir Zonshine")]
// The product name that this plugin is part of
[assembly: AssemblyProduct("2-Point Polar Alignment")]
[assembly: AssemblyCopyright("Copyright © 2026 Nir Zonshine")]

// The minimum Version of N.I.N.A. that this plugin is compatible with
[assembly: AssemblyMetadata("MinimumApplicationVersion", "3.1.0.0")]

// The license your plugin code is using
[assembly: AssemblyMetadata("License", "MIT")]
// The url to the license
[assembly: AssemblyMetadata("LicenseURL", "https://opensource.org/licenses/MIT")]
// The repository where your pluggin is hosted
[assembly: AssemblyMetadata("Repository", "https://github.com/nirzons/TwoPointPolarAlignment")]

// The following attributes are optional for the official manifest meta data

//[Optional] Your plugin homepage URL - omit if not applicaple
[assembly: AssemblyMetadata("Homepage", "https://github.com/nirzons/TwoPointPolarAlignment")]

//[Optional] Common tags that quickly describe your plugin
[assembly: AssemblyMetadata("Tags", "")]

//[Optional] A link that will show a log of all changes in between your plugin's versions
[assembly: AssemblyMetadata("ChangelogURL", "https://github.com/nirzons/TwoPointPolarAlignment/blob/main/CHANGELOG.md")]

//[Optional] The url to a featured logo that will be displayed in the plugin list next to the name
[assembly: AssemblyMetadata("FeaturedImageURL", "")]
//[Optional] A url to an example screenshot of your plugin in action
[assembly: AssemblyMetadata("ScreenshotURL", "")]
//[Optional] An additional url to an example example screenshot of your plugin in action
[assembly: AssemblyMetadata("AltScreenshotURL", "")]
//[Optional] An in-depth description of your plugin
[assembly: AssemblyMetadata("LongDescription", @"")]

// Setting ComVisible to false makes the types in this assembly not visible
// to COM components.  If you need to access a type in this assembly from
// COM, set the ComVisible attribute to true on that type.
[assembly: ComVisible(false)]
// [Unused]
[assembly: AssemblyConfiguration("")]
// [Unused]
[assembly: AssemblyTrademark("")]
// [Unused]
[assembly: AssemblyCulture("")]