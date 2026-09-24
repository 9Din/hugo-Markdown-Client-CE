<p align="center">
<a href="README.md">English</a> · <a href="README.zh.md">简体中文</a>
</p>

# Hugo - Markdown Client

A desktop application for Hugo content management, enabling non-technical users to easily write, edit, preview, and publish Hugo website content. Built with **WPF + .NET 8**, it embeds Hugo and an **AI assistant (DeepSeek)** for an all-in-one writing workflow.

##  Features

###  AI Assistant (DeepSeek)
- Chat with a **built-in AI assistant** (DeepSeek Chat Completions, OpenAI-compatible API)
- **Streaming output** with Cline-style bubbles (user right-aligned, assistant full-width Markdown)
- Supports **Markdown rendering** (headings / lists / tables / code blocks / links) and **emoji**
- **All messages are copyable** — select text with the mouse, or click the "⧉ Copy" button
- Buttons to **analyze the current file** or **analyze the project structure** (content/static/assets only)
- **AI can create Markdown articles and folders** directly into `content/` — just ask "write a post about ..." and the file appears in the tree
- Configure API key/base URL/model via the **API Settings** dialog (⚙ in the AI panel)
- AI panel **collapses by default** — click the **AI** button at the top-right of the editor to expand
- **Panel width memory** — drag to resize, saved automatically; restores after collapse/expand

###  Hugo Project Navigation
- Open a Hugo project folder and browse content in a tree view
- Only the `content`, `static`, and `assets` folders are listed to avoid clutter
- Create files, create folders, rename, and delete
- **Refresh** button to reload newly imported files
- **Right-click context menu** for quick file/folder operations, template creation, and "Show in File Explorer"
- File tree **collapses by default**, expand on demand

###  Markdown Editor
- Focused two-pane layout: file tree on the left, editor on the right
- Save / Save As with unsaved-changes prompt, **Ctrl+S** quick save
- Auto-generates Hugo frontmatter (title, date) for new files
- **Formatting toolbar** for bold, italic, strikethrough, headings, links, images, quotes, code blocks, lists, horizontal rules, and tables
- **Friendly Frontmatter editor** — edit metadata fields via a form (instead of raw YAML), with automatic YAML generation
- **Light and dark themes** inspired by VS Code color schemes, applied globally (including dialogs), defaults to system dark/light mode

###  Hugo Command Control
- One-click start / stop `hugo server` with real-time logs
- Starting the server automatically opens the live site in your default browser (localhost:1313)
- **Flexible Hugo discovery** (in priority order):
  1. **User-selected path** — when Hugo is not found, the app asks the user to select `hugo.exe` manually (saved for future use)
  2. **Embedded Hugo** — `build.bat` can optionally embed `hugo.exe` in the publish folder (no separate installation needed)
  3. **Portable fallback** — `hugo.exe` next to the app, or in `vendor/hugo/hugo.exe`
  4. **System PATH** — if Hugo is already installed
- **Auto-download on first use**: if Hugo is not found and the user chooses to download, the app downloads `hugo.exe` (~50 MB) automatically with **real-time download progress** (percentage / MB)
- Git commit & push (add → commit → push)

###  Folder Templates
- Automatically uses `_template.md` from the current or parent directory as a starting point for new files
- Right-click a folder → **New Template** to create a template
- New files created from a template automatically open the Frontmatter editor for easy metadata entry

###  Folder Mirrors (Multilingual)
- Automatically detects language folders under `content` (en, zh, fr, etc.)
- One-click copy current file to target language folder, preserving relative path structure
- After mirroring, the target file is automatically opened for further editing

###  Bilingual UI
- One-click switch between Chinese and English
- All buttons, labels, and tooltips update synchronously

##  Tech Stack

- **.NET 8** (WPF)
- **Hugo** (static site generator, **embedded** — no separate installation needed)
- **DeepSeek API** (AI assistant, OpenAI-compatible chat completions)

##  Requirements

### For users (published build)
- **Windows 10/11** (x64)
- No .NET installation needed (self-contained single-file exe)
- **No Hugo installation needed** (hugo.exe embedded; auto-downloads with progress if missing)
- **Git** (only required for the "Commit & Push" feature)

### For developers (build from source)
- **.NET 8 SDK** — download from https://dotnet.microsoft.com/download/dotnet/8.0
- Internet access on first build (`build.bat` auto-downloads Hugo 0.157.0 extended into the publish folder)

### AI feature prerequisites
- A **DeepSeek API Key** (get one at https://platform.deepseek.com)
- Open the AI panel (top-right **AI** button) → click **⚙** → enter API Key / Base URL / Model

##  Usage

1. Run `Huge.exe`
2. Click **"Open Hugo Project"** and select the project folder
3. Select a file in the left tree view to edit
4. Use the formatting toolbar for quick Markdown styling
5. Click **"⚙ Frontmatter"** to edit metadata through a friendly form
6. Click **"🌙"** to toggle between light and dark themes
7. Click **"▶ Start Hugo"** to start the server — the site opens automatically in your browser
8. (Optional) Click the **AI** button at the top-right of the editor to open the AI assistant

### Using the AI assistant
1. Click the **AI** button (top-right of the editor) to expand the panel
2. Click **⚙** and enter your **DeepSeek API Key** (and model, e.g. `deepseek-chat`)
3. Type a question and press **Enter** (Shift+Enter for newline)
4. Quick actions: **"分析当前文件"** / **"分析项目结构"** buttons above the input
5. Ask AI to write content: *"帮我写一篇关于 XXX 的 posts 文章"* — the app creates the `.md` file in `content/posts/` automatically

##  Build & Package

Run `build.bat` to automatically build and publish as a single-file exe:

```bat
build.bat
```

During the build, you'll be asked whether to embed `hugo.exe` into the publish folder:
- **Y** → embeds Hugo (~50 MB extra, zero-dependency for users)
- **N** (default) → smaller exe; users can select `hugo.exe` manually when starting the server, or the app will auto-download it on first use

Output is located at `publish\Huge.exe` (self-contained, no .NET installation required). Copy the entire `publish` folder to any Windows computer and everything works out of the box.

##  Project Structure

```
Huge/
├── App.xaml              # Application entry & global styles
├── App.xaml.cs
├── MainWindow.xaml       # Main window UI
├── MainWindow.xaml.cs    # Main window logic
├── ApiSettings.cs        # DeepSeek API settings (save/load)
├── ApiSettingsDialog.xaml / .cs   # API settings dialog
├── DeepSeekClient.cs     # DeepSeek Chat Completions client (streaming)
├── MarkdownRenderer.cs   # Lightweight Markdown → FlowDocument renderer
├── Huge.csproj           # Project configuration
├── build.bat             # Build script
├── embed-hugo.ps1        # Embeds hugo.exe into publish folder
├── Assets/
│   └── huge.ico          # Application icon
└── publish/              # Publish output (generated by build)
```

##  License

This project is developed based on the concept of [Huge - Hugo Genie](https://github.com/romeolefter-cpu/huge). Free and open source, welcome to use and contribute.
