using System.Text.RegularExpressions;
using Ehr.Testing;
using Xunit;

namespace Ehr.Web.Tests.TestCases.UC9;

// Test Case ID: TC-UC9-05
// Purpose / scenario: Speech recognition and the language model run only on the team's server and can't send a
//   dictation anywhere else: their containers sit on a network with no route out, publish no port, and only the
//   on-prem app shares that network with them. (That a whole dictation logs none of its words is the dictation
//   service's own test: TC_UC9_05_Nfr07_LocalModels.md.)
// Type: Non-functional (privacy)
// Requirements: NFR-07
// Use case: UC 9, Transcribe dictated audio, and UC 10, Structure and validate note fields; scope: the dictation
//   service, llama-server and the inference network
// Preconditions: The local server stack's definition, infra/compose/compose.yaml.
// Inputs and system conditions: The compose file as committed; no containers need to run.
// Test data: The services "dictation" (MedASR and the checks) and "llama" (K2 Horizon 7B), and the network "inference".
// Steps:
//   1. Read the "inference" network's definition.
//   2. Read the networks and ports of "dictation" and "llama".
//   3. List every service on the "inference" network.
// Expected result: "inference" is internal: true, so nothing on it has a route to the internet. "dictation" and
//   "llama" are on "inference" only, and neither publishes a port. The only other service on "inference" is
//   "onprem-app", the one caller.
// Creator: Samir Mukshe
public partial class TC_UC9_05_Nfr07_LocalModels
{
    static readonly string Compose = File.ReadAllText(RepositoryRoot.Combine("infra/compose/compose.yaml")).ReplaceLineEndings("\n");

    [Fact]
    public void The_models_run_on_a_network_with_no_route_out_and_no_published_port()
    {
        Assert.Matches(InferenceNetwork(), Compose);
        foreach (var model in new[] { "dictation", "llama" })
        {
            var service = Service(model);
            Assert.Equal(["inference"], Networks(service));
            Assert.DoesNotMatch(Ports(), service);
        }

        var onInference = Services().Where(name => Networks(Service(name)).Contains("inference")).Order(StringComparer.Ordinal);
        Assert.Equal(["dictation", "llama", "onprem-app"], onInference);
    }

    // A top-level service's block: from "  name:" to the next line indented two spaces, or a top-level key.
    static string Service(string name)
    {
        var match = Regex.Match(Compose, $@"(?ms)^  {Regex.Escape(name)}:\n(.*?)(?=^  \S|^\S)");
        Assert.True(match.Success, $"compose.yaml has no service \"{name}\".");
        return match.Groups[1].Value;
    }

    static IEnumerable<string> Services()
    {
        var services = Regex.Match(Compose, @"(?ms)^services:\n(.*?)(?=^\S)").Groups[1].Value;
        return Regex.Matches(services, @"(?m)^  ([a-z][a-z0-9-]*):$").Select(m => m.Groups[1].Value);
    }

    // Written either as a list in brackets or as one key per line ("wan" has aliases under its keys).
    static string[] Networks(string service)
    {
        var list = Regex.Match(service, @"(?m)^    networks: \[([^\]]*)\]");
        if (list.Success)
        {
            return [.. list.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];
        }

        var keys = Regex.Match(service, @"(?ms)^    networks:\n((?:      .*\n|        .*\n)*)");
        return [.. Regex.Matches(keys.Groups[1].Value, @"(?m)^      ([a-z][a-z0-9-]*):").Select(m => m.Groups[1].Value)];
    }

    [GeneratedRegex(@"(?m)^networks:\n(?:  .*\n|\n)*?  inference:\n    internal: true$")]
    private static partial Regex InferenceNetwork();

    [GeneratedRegex(@"(?m)^    ports:")]
    private static partial Regex Ports();
}
