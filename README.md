# MCPDesk

A Windows-native desktop client for [Model Context Protocol (MCP)](https://modelcontextprotocol.io/). Connect to any MCP server, use any LLM provider, and orchestrate AI tools — all from one app.

![WinUI 3](https://img.shields.io/badge/WinUI%203-.NET%208-blue)
![License](https://img.shields.io/badge/license-MIT-green)

## Why MCPDesk?

Every existing MCP client is either locked to one LLM (Claude Desktop) or is a code editor (VS Code, Cursor, Zed). **MCPDesk fills the gap**:

| Feature | MCPDesk | Claude Desktop | VS Code + Continue | Cursor |
|---------|---------|---------------|-------------------|--------|
| Multi-LLM provider | ✅ | ❌ Claude only | ✅ | ✅ |
| GitHub Copilot support | ✅ | ❌ | ✅ | ❌ |
| Non-developer friendly | ✅ | ✅ | ❌ | ❌ |
| Windows native (WinUI 3) | ✅ | ❌ Electron | ❌ | ❌ Electron |
| Multi-MCP server | ✅ | ✅ | ✅ | Limited |
| Mica backdrop | ✅ | ❌ | ❌ | ❌ |

## Features

- **Multi-Provider LLM Support** — OpenAI, Anthropic Claude, GitHub Copilot, and Ollama (local). Switch providers and models on the fly.
- **GitHub Copilot Integration** — Sign in with your existing Copilot subscription via OAuth device flow. No extra API keys needed.
- **Dynamic Model Selector** — Browse all models available in your subscription and switch instantly.
- **MCP Server Management** — Configure, connect, and manage multiple MCP servers. Auto-connects enabled servers on startup.
- **Tool Call Orchestration** — The LLM sees tools from ALL connected MCP servers. Multi-iteration tool calling with real-time status updates.
- **Conversation Persistence** — SQLite-backed chat history with conversation management (create, switch, delete).
- **Windows 11 Native** — WinUI 3 with Mica backdrop, NavigationView, and proper Windows design language.
- **Config Auto-Detection** — Paste standard MCP config formats and MCPDesk normalizes them automatically.

## Quick Start

### Prerequisites

- Windows 10 (1809+) or Windows 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Windows App SDK](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/)

### Build & Run

```bash
git clone https://github.com/clatonhendricks/MCPDeskClient.git
cd MCPDeskClient
dotnet restore src/MCPClient/MCPClient.csproj
dotnet build src/MCPClient/MCPClient.csproj
dotnet run --project src/MCPClient/MCPClient.csproj
```

### Configuration

MCPDesk stores its configuration at `%APPDATA%\MCPDesk\config.json`:

```json
{
  "mcpServers": {
    "kusto-mcp": {
      "command": "uvx",
      "args": ["azure-kusto-mcp"],
      "env": {
        "KUSTO_SERVICE_URI": "https://your-cluster.kusto.windows.net/"
      },
      "enabled": true
    },
    "filesystem": {
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-filesystem", "C:\\Users\\you\\Documents"],
      "env": {},
      "enabled": true
    }
  },
  "llmProviders": {
    "github-copilot": {
      "type": "GitHubCopilot",
      "apiKey": "",
      "model": "gpt-4o",
      "enabled": true
    }
  },
  "defaultProviderId": "github-copilot"
}
```

### GitHub Copilot Setup

1. Open MCPDesk → Settings
2. Click **Sign in with GitHub**
3. Enter the code shown at [github.com/login/device](https://github.com/login/device)
4. Done — your Copilot subscription is now the LLM backend

### Adding MCP Servers

**Via config.json** — Edit `%APPDATA%\MCPDesk\config.json` and add entries under `mcpServers`.

**Via UI** — Navigate to MCP Servers → Add Server → fill in command, args, and environment variables.

## Architecture

```
MCPClient/
├── src/
│   ├── MCPClient/                    # WinUI 3 app
│   │   ├── Views/                    # ChatPage, SettingsPage, ServerConfigPage
│   │   ├── ViewModels/               # MVVM with CommunityToolkit.Mvvm
│   │   ├── Converters/               # XAML value converters
│   │   └── App.xaml                  # DI container, global error handling
│   └── MCPClient.Core/               # Business logic (no UI dependency)
│       ├── LlmProviders/             # OpenAI, Anthropic, Ollama, GitHubCopilot
│       ├── Services/                 # MCP client, config, LLM, conversation
│       └── Models/                   # AppConfig, Conversation, Message
└── tests/
    └── ToolCallTest/                 # Integration test for tool call loop
```

### Key Technical Details

- **Copilot API**: OAuth tokens are exchanged for Copilot JWT tokens via `api.github.com/copilot_internal/v2/token`. Chat uses direct HTTP to `{endpoint}/chat/completions` (not OpenAI SDK, which prepends `/v1/`).
- **Tool Names**: MCP tool names use `__` separator (`server__tool`) to stay within OpenAI's `[a-zA-Z0-9_-]` function name constraint.
- **Provider Persistence**: Provider instances are reused across page navigations to preserve auth state.

## Supported LLM Providers

| Provider | Auth Method | Models |
|----------|------------|--------|
| GitHub Copilot | OAuth device flow or PAT | All subscription models (GPT-4o, Claude, Gemini, etc.) |
| OpenAI | API key | GPT-4o, GPT-4o mini, o3-mini |
| Anthropic | API key | Claude Sonnet 4, Claude Opus 4, Claude Haiku 3 |
| Ollama | Local endpoint | Any locally installed model |

## Dependencies

- [Microsoft.WindowsAppSDK](https://www.nuget.org/packages/Microsoft.WindowsAppSDK) — WinUI 3
- [CommunityToolkit.Mvvm](https://www.nuget.org/packages/CommunityToolkit.Mvvm) — MVVM framework
- [ModelContextProtocol](https://www.nuget.org/packages/ModelContextProtocol) — MCP C# SDK
- [Microsoft.EntityFrameworkCore.Sqlite](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore.Sqlite) — Conversation persistence
- [OpenAI](https://www.nuget.org/packages/OpenAI) — OpenAI/GitHub Models SDK
- [Anthropic.SDK](https://www.nuget.org/packages/Anthropic.SDK) — Anthropic SDK

## Roadmap

- [ ] MCP Server Gallery — Browse and one-click install popular MCP servers
- [ ] Streaming responses — Show tokens as they arrive
- [ ] Workflow/Agent mode — Save repeatable tool chains
- [ ] Export to Markdown/PDF
- [ ] MSIX packaging for enterprise deployment
- [ ] Conversation search
- [ ] System tray / hotkey quick launch
- [ ] Markdown rendering in chat

## License

GPL-3.0
