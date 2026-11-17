using System.Runtime.InteropServices;
using Rhino.PlugIns;

// Shown in Rhino's Options > Plug-ins page.
[assembly: PlugInDescription(DescriptionType.Organization, "Robin Song Design")]
[assembly: PlugInDescription(DescriptionType.WebSite, "https://github.com/RobinSongDesign/RhinoAIRenderer")]
[assembly: PlugInDescription(DescriptionType.Icon, "Chameleon.EmbeddedResources.plugin-utility.ico")]

// Same id as the WPF version, so Rhino treats this build as an upgrade rather than a second plug-in.
[assembly: Guid("da1c6445-8347-41bf-8774-8c264cadf31d")]
