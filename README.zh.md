# Hugo - Markdown Client

一个面向 Hugo 内容管理者的桌面应用，让非技术用户也能轻松编写、编辑、预览和发布 Hugo 网站内容。

## ✨ 功能特性

### 📁 Hugo 项目导航
- 打开 Hugo 项目文件夹，以树形视图浏览内容
- 仅显示 `content`、`static`、`assets` 三个核心目录，避免杂乱目录干扰
- 支持新建文件、新建文件夹、重命名、删除
- **右键上下文菜单**，快速进行文件/文件夹操作和模板创建

### ✍️ Markdown 编辑器
- 简洁双栏布局：左侧文件树 + 右侧编辑器
- 保存 / 另存为，未保存更改自动提示
- 新建文件自动生成 Hugo frontmatter（title、date）
- **格式化工具栏**：粗体、斜体、删除线、标题、链接、图片、引用、代码块、列表、分割线、表格
- **友好的 Frontmatter 编辑器**：通过表单编辑元数据（而非直接操作 YAML），自动生成 YAML 格式
- **亮/暗双主题**：仿 VS Code 配色，全局生效（含对话框），默认跟随系统深色/亮色模式

### 🚀 Hugo 命令控制
- 一键启动 / 停止 `hugo server`，日志实时显示
- 启动服务器后自动在默认浏览器打开站点（localhost:1313）
- **Hugo 已嵌入**：`build.bat` 会自动下载最新版 Hugo（windows-amd64）并放入发布目录，任何电脑无需单独安装 Hugo 或配置 PATH
- **便携模式**：若 `hugo.exe` 放在程序同目录（或 `vendor/hugo/hugo.exe`），也会自动使用
- Git 提交推送（add → commit → push）

### 📂 文件夹模板
- 新建文件时自动查找当前目录及上级目录的 `_template.md` 作为模板起点
- 右键文件夹 → **新建模板** 即可创建模板
- 使用模板新建文件后自动弹出 Frontmatter 编辑器，方便填写元数据
- 适合有重复内容结构的站点（产品页、新闻、文档等）

### 🌐 文件夹镜像（多语言）
- 自动检测 `content` 下的语言文件夹（en、zh、fr 等）
- 一键将当前文件复制到目标语言文件夹，保持相对路径结构
- 镜像复制后自动打开目标文件以便继续编辑
- 简化多语言站点维护

### 🌍 中英文切换
- 界面支持中英文一键切换，所有按钮、标签、提示同步更新

## 🛠️ 技术栈

- **.NET 8**（WPF）
- **Hugo**（静态站点生成器，需单独安装）

## 📦 环境要求

- **Windows 10/11**（x64）
- **.NET 8 Runtime**（发布版已内置，无需安装）
- **Hugo**（使用 Hugo 命令功能时需要，需加入 PATH）
- **Git**（使用提交推送功能时需要）

## 🚀 使用说明

1. 运行 `Huge.exe`
2. 点击 **"打开 Hugo 项目"** 选择项目文件夹
3. 在左侧文件树中选择文件进行编辑
4. 使用格式化工具栏快速设置 Markdown 样式
5. 点击 **"⚙ Frontmatter"** 通过友好表单编辑元数据
6. 点击 **"🌙"** 可切换亮/暗主题
7. 点击 **"▶ 启动 Hugo"** 启动服务器，站点将自动在浏览器中打开

## 🔨 构建打包

运行 `build.bat` 即可自动构建、发布为单文件 exe，并自动下载嵌入最新版 Hugo：

```bat
build.bat
```

输出文件位于 `publish\Huge.exe`（self-contained，无需安装 .NET 即可运行），同时 `publish\hugo.exe` 已嵌入其中。将整个 `publish` 文件夹拷贝到任意 Windows 电脑即可开箱即用。

## 📁 项目结构

```
Huge/
├── App.xaml              # 应用入口与全局样式
├── App.xaml.cs
├── MainWindow.xaml       # 主窗口界面
├── MainWindow.xaml.cs    # 主窗口逻辑
├── Huge.csproj           # 项目配置
├── build.bat             # 打包脚本
├── Assets/
│   └── huge.ico          # 应用图标
└── publish/              # 发布输出（构建生成）
```

## 📄 License

本项目基于 [Huge - Hugo Genie](https://github.com/romeolefter-cpu/huge) 的理念开发，免费开源，欢迎使用与贡献。