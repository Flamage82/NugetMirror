using System.ComponentModel;
using Spectre.Cli;

namespace NugetMirror.Application.Mirror
{
    public class MirrorCommandSettings : CommandSettings
    {
        [CommandArgument(0, "<Source>")]
        [Description("The source Nuget URL")]
        public string Source { get; set; }

        [CommandArgument(1, "<Destination>")]
        [Description("The destination Nuget URL")]
        public string Destination { get; set; }

        [CommandArgument(2, "<ApiKey>")]
        [Description("The destination Nuget API key")]
        public string ApiKey { get; set; }

        [CommandOption("-s|--search <Search>")]
        [Description("A search term to filter packages")]
        public string SearchTerm { get; set; }

        [CommandOption("-u|--upload-timeout <UploadTimeout>")]
        [Description("The timeout in seconds for uploading each package")]
        [DefaultValue(300)]
        public int UploadTimeout { get; set; }
    }
}
