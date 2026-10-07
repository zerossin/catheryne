using System;
using System.Collections.Generic;
using System.Threading.Tasks;

// Wire/session differences only. The chat host owns policy, tools, cards and stopping.
internal interface IAiProvider : IDisposable {
 Task Start();
 Task Login();
 Task<Dictionary<string,object>> Call(string method,Dictionary<string,object> args);
}

internal static class AiProviders {
 internal const string Codex="chatgpt",Claude="claude";
 internal static bool IsChat(string provider){return provider==Codex||provider==Claude;}
 internal static string Preference(string provider,string key){return provider==Codex?key:"claude"+key.Substring(2);}
 internal static bool LoginAddress(string url,string provider){Uri address;return Uri.TryCreate(url,UriKind.Absolute,out address)&&address.Scheme=="https"&&address.IsDefaultPort&&address.UserInfo.Length==0&&(provider==Codex?(address.Host=="auth.openai.com"||address.Host=="chatgpt.com"):(address.Host=="claude.ai"||address.Host=="platform.claude.com"||address.Host=="console.anthropic.com"));}
 internal static string Instructions(string provider){return CodexChat.CurrentInstructions()+ (provider==Claude?"\nProvider capabilities: Claude Code via MCP exposes only the listed Catheryne tools. Native goal and request_user_input tools are unavailable. Catheryne MCP returns standard image content. The code-mode string parsing example applies only to Codex code mode.":"");}
}
