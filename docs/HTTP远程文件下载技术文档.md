# HTTP 远程文件下载器（ktdl）技术文档

- **仓库**：<https://github.com/KaelTian/KTDownloader.git>
- **整理日期**：2026-09-24
- **一句话**：把「一个静态站点上的一批文件」批量、可断点续传、可跳过已完成地拉到本地。
- **技术栈**：.NET 10（`net10.0` / `net10.0-windows`）、WinForms、xUnit、`System.Text.Json`；不依赖任何第三方包。
- **验证环境**：内网 IIS 10.0（`http://192.168.0.189:9526`，物理路径 `C:\Uploads\images`），串行实测 **14 个文件 / 807.8 MB / 约 39 秒**（约 21 MB/s）。

---

## 目录

1. [快速上手](#1-快速上手)
2. [工程结构](#2-工程结构)
3. [下载引擎：HTTP 协议是怎么用的](#3-下载引擎http-协议是怎么用的)
4. [.part 断点文件与原子改名](#4-part-断点文件与原子改名)
5. [清单机制：为什么必须要有清单](#5-清单机制为什么必须要有清单)
6. [清单生成端：本地目录 → files.json](#6-清单生成端本地目录--filesjson)
7. [批量调度](#7-批量调度)
8. [UI 线程模型与进度回调](#8-ui-线程模型与进度回调)
9. [测试与真实验证](#9-测试与真实验证)
10. [已知缺口与没做的事](#10-已知缺口与没做的事)
11. [技术储备记录](#11-技术储备记录)
12. [踩过的坑（经验沉淀）](#12-踩过的坑经验沉淀)

---

## 1. 快速上手

### 1.0 先搞清楚名字和怎么跑起来

`ktdl` / `ktdl-ui` / `ktdl-manifest` 是三个项目在 csproj 里写的 **`<AssemblyName>`**，也就是编译产物的名字，**不是** `dotnet run --project` 的简写。所以：

- 本文档下面出现的 `ktdl <地址> <路径>` 是**产物已经被放到 PATH 上（或发布成单文件）之后**的形态
- 从源码直接跑，得写全路径。注意 `--` 分隔符 —— 它后面的参数才是传给程序自己的

```bash
# 命令行下载器（注意 -- 后面才是 ktdl 的参数，写错会把 dotnet 的参数喂给程序）
dotnet run --project KT.Downloader.Cli -- <下载地址> <保存路径>
dotnet run --project KT.Downloader.Cli -- -h

# 或者直接跑编译好的产物（Windows）
KT.Downloader.Cli/bin/Debug/net10.0/ktdl.exe -h

# 两个 WinForms
dotnet run --project KT.Downloader.WinForms        # ktdl-ui
dotnet run --project KT.Downloader.ManifestBuilder # ktdl-manifest
```

顺手记一个自己撞过的坑：把 `--nologo` 之类的 dotnet 选项写在 `--` 后面，会被当成程序参数、报「未知参数」—— 参数位置错了，报错却像是程序本身的 bug。

打包发布（Day 11 那类活）见 [储备 D.1](#d1-打包发布与剪裁)。

### 1.1 命令行：单文件下载（`ktdl`）

```bash
ktdl <下载地址> <保存路径>          # 从头下
ktdl -c <下载地址> <保存路径>       # 接着上次没下完的进度继续下
ktdl -h                            # 帮助
```

退出码：`0` 成功 / `1` 下载失败 / `2` 参数用法错误 / `130` 被 Ctrl+C 中断。

### 1.2 WinForms 客户端：批量下载（`ktdl-ui`）

1. 填清单地址（例如 `http://192.168.0.189:9526/files.json`）→ **加载清单**
2. 选下载目录 → **开始下载**（随时 **停止**，当前文件保留 `.part`）
3. 表格逐行显示每个文件的状态与进度；本地已有完整副本的行会标成「已存在」并跳过

### 1.3 WinForms 生成器：批量造清单（`ktdl-manifest`）

站点的目录里放一个 `files.json`，就是下载端要读的那份清单 —— 这个工具负责把「一个目录」扫成「一份清单」。

1. **站点根目录**：填 IIS 站点绑定的物理路径（如 `C:\Uploads\images`）
2. **清单文件名**：默认 `files.json`
3. **扫描** → 表格列出所有相对路径与大小 → **生成清单**（写进站点根目录）

站点里文件更新后，重跑一遍扫描 + 生成即可。生成结果按路径排序，同一目录多次生成的输出是稳定的，改了哪个文件一眼能从 diff 看出来。

> 两个 WinForms 程序角色不同，可以分别发布：生成器跟着**放文件的那台机器**走，下载端跟着**要用文件的人**走。

---

## 2. 工程结构

```text
KT.Downloader.Cli/              ← 核心库 + 命令行入口（AssemblyName: ktdl）
├── CommandLine/                参数解析（-c / -h、URL 校验、错误文案）
├── Downloading/                单文件下载引擎
│   ├── HttpFileDownloader      ★ HTTP 请求、Range 续传、.part 落盘、原子改名
│   ├── IFileDownloader          下载抽象（便于测试替身与后续换实现）
│   ├── DownloadRequest/Result   入参 / 三态结果（Completed | Canceled | Failed）
├── Manifests/                  清单两端
│   ├── HttpRemoteManifestLoader ★ 拉远端清单、解析、路径与安全校验
│   ├── LocalManifestBuilder     ★ 扫本地目录、生成清单（生成器用）
│   └── RemoteFileEntry          加载端条目：相对路径 + 已解析的绝对 URL + 大小
├── Bulk/                       批量调度
│   ├── BatchDownloader          ★ 顺序调度、跳过已完成、单文件失败不停
│   ├── LocalPathMapper          相对路径 → 本地绝对路径（防路径穿越）
├── Progress/                   进度条、速度计、字节格式化
└── App.cs / Program.cs         组合根：解析参数 → 调引擎 → 翻译退出码

KT.Downloader.WinForms/         ← 批量下载客户端 UI 壳（ktdl-ui）
KT.Downloader.ManifestBuilder/  ← 清单生成器 UI 壳（ktdl-manifest）
KT.Downloader.Cli.Tests/        ← xUnit，143 条
```

**分层原则**：所有逻辑都在 `Cli` 库里，两个 UI 只是壳。UI 壳里没有一行下载或解析逻辑 —— 所以核心逻辑全都能在无 UI 的情况下单测。

**依赖方向**：`UI → Cli`，单向。`Bulk` 依赖 `Downloading` + `Manifests` 的抽象接口，不依赖具体实现。

---

## 3. 下载引擎：HTTP 协议是怎么用的

核心在 `Downloading/HttpFileDownloader.cs`，一次下载的完整流程：

```text
1. 看本地有没有 <输出路径>.part          → 有就记下长度作为续传起点 resumeFrom
2. GET 请求；resumeFrom > 0 时带 Range: bytes=<resumeFrom>-
3. 判断返回码：
     416 → 断点已经到头了，或远端变小了（靠 Content-Range 里那个 "bytes */总长" 分辨）
     206 → 正常续传，总大小取 Content-Range 的 Length
     200 → 服务器不理 Range（不支持续传），把 resumeFrom 归零，从头写
4. HttpCompletionOption.ResponseHeadersRead：拿到响应头就返回，正文边读边写
5. 64 KB 缓冲循环读写，每写一块报一次进度
6. 全下完 → File.Move(.part, 正式文件, overwrite: true)
```

### 3.1 几个容易踩的协议细节

| 点 | 处理方式 | 不这么做会怎样 |
| --- | --- | --- |
| **206 的 `Content-Length`** | 它只是**这一段**的长度，总大小要去 `Content-Range` 的 `Length` 拿 | 进度条永远停在个位数百分比 |
| **要了 Range 却回了 200** | 正文是完整文件，必须把 `resumeFrom` 归零、`FileMode.Create` 重写 | 完整内容被追加到 `.part` 后面，拼出一个又长又烂的文件 |
| **416 Range Not Satisfiable** | 用响应里的 `Content-Range: bytes */总长` 和本地断点比对：相等 → 断点即完整文件，直接改名完事；不等 → 远端文件变了，报错让人删掉重下 | 把「恰好下完」误判成失败；或把对不上的两个东西拼在一起 |
| **`Content-Length` 缺失**（分块传输） | 允许 `TotalBytes` 为 null，进度条退化成只显示已下载字节数 | 直接进度条崩掉 |
| **取消 vs 超时** | 同一个 `OperationCanceledException` 来自两种原因，靠 `cancellationToken.IsCancellationRequested` 分流：用户取消 → `Canceled`，HttpClient 超时 → `Failed` | 超时被报成「已取消」，退出码也跟着错 |
| **中文与空格文件名** | 清单里的路径**逐段** `Uri.EscapeDataString` 后拼进 URL（分隔符 `/` 要留着，否则整条路径会被压成一段） | 400 / 404，且中文站点全挂 |

### 3.2 为什么用 `ResponseHeadersRead`

默认的 `ResponseContentRead` 会把整个响应体缓冲进内存再交给你 —— 对一个 660 MB 的文件意味着 660 MB 内存。`ResponseHeadersRead` 让正文变成流，边收边写盘，内存占用恒定为 64 KB 缓冲。

配合 `FileOptions.SequentialScan` 告诉操作系统「这块文件是按顺序读写的」，减少页缓存抖动。

---

## 4. `.part` 断点文件与原子改名

**规则：先写 `<输出路径>.part`，全部成功后才 `File.Move` 成正式文件。**

由此得到两条重要性质：

1. **正式文件的存在 == 内容完整**。任何时刻断电、断网、被 kill，磁盘上要么是完整文件，要么是 `.part`，绝不会出现「半截的正式文件」被下游程序当完整文件读走。
2. **断点续传免费获得** —— 下次 `-c` 的起点就是 `.part` 的当前长度。

**取消和失败时，`.part` 一律保留。** 这是刻意的：用户按 Ctrl+C 的意图是「先停下」，不是「把已下的 300 MB 扔掉」。清理 `.part` 等于抹掉进度。

> 这里有个设计要求：`File.Move` 必须在文件句柄释放之后执行。代码里用一对花括号划出 `FileStream` 的作用域，就是为了让 `await using` 先释放句柄再改名 —— 否则 Windows 上会撞 `IOException: 文件正被另一进程使用`。

---

## 5. 清单机制：为什么必须要有清单

### 5.1 起因：那台 IIS 不给列目录

原本想要的能力是「把站点某个目录整个拉下来」。但目标服务器实测：

- 根目录和子目录请求都返回 **403.14**（IIS 目录浏览默认关闭）
- `OPTIONS /` 只返回 `OPTIONS, TRACE, GET, HEAD, POST` —— **没有 `PROPFIND`**（WebDAV 没装）

结论：**HTTP 层面根本列不出目录。** 清单文件是唯一既不依赖服务器配置改动、又能跑在任意静态服务器上的做法。

### 5.2 清单格式

```json
{
  "files": [
    { "path": "download_test/Docker Desktop Installer.exe", "size": 660773808 },
    { "path": "L1/D1/2026/202605/20260520/条码.jpg", "size": 49377 }
  ]
}
```

| 字段 | 约定 |
| --- | --- |
| `files` | 数组，顺序即下载顺序 |
| `path` | **相对路径**：一律正斜杠，相对**清单所在目录**（不是站点根！），放子目录里也是这个规则 |
| `size` | 字节数，**可选**；给了就能判断本地副本是否完整，没给就只能重下 |

### 5.3 清单是远端给的，所以不能信

清单文件在服务器上，理论上可能被改。两条校验是硬性的（都有测试）：

- **拒绝绝对地址**：`http://evil.example/payload.exe` 这种条目能决定请求打到哪台机器，不能让远端说了算
- **拒绝 `..` 逃逸**：`download_test/../../escape.exe` 这种条目能决定文件落到哪个目录，必须拦

本地侧的兜底在 `LocalPathMapper`：`Path.Combine` 碰到绝对路径会**静默丢掉前面的根**，所以必须先判 `IsPathRooted`，再用 `Path.GetFullPath` 消解 `..`，最后比对结果是否还在根目录边界内。判定只比对最终绝对路径 —— 不用去猜输入长什么样。

### 5.4 为什么本地路径要镜像远端目录结构

平铺到同一个目录会撞重名（远端六个层级的路径里可能有同名文件）。所以 `L1/D1/2026/202605/20260520/1.jpg` 落到本地也是这六层目录，一一对应。

---

## 6. 清单生成端：本地目录 → files.json

`Manifests/LocalManifestBuilder.cs` 是加载端（`HttpRemoteManifestLoader`）的反面：

```text
选目录 → EnumerateFiles(递归) → 转成相对路径（正斜杠）→ 读文件大小 → 按路径排序 → 序列化成 JSON
```

三个约定：

- **排除清单文件自身**。它就在站点根下躺着，扫进去的话下载端会去下它自己，而它每次生成都在变
- **相对路径用正斜杠**，与加载端的解析规则严格对齐
- **按路径序号排序**，保证重跑输出稳定（否则每次顺序不同，diff 全是噪音）

**两端的一致性由测试锁住**：`ToJson_ProducesAManifestTheLoaderReadsBack` 把「生成端写出来的 JSON」直接喂给真实的加载端解析，断言路径、大小、URL 全部对得上。哪一端改了字段名或路径语义，这条测试立刻变红。

### 6.1 真机对拍验证

拿生成器和线上 IIS 对过账（一次性验证程序，不属于仓库）：

```text
扫本地目录 C:\Uploads\images：14 个文件
拉线上清单 http://192.168.0.189:9526/files.json：14 个文件
结果：完全一致 ✔        ← 路径集合、大小逐条相同
```

---

## 7. 批量调度

`Bulk/BatchDownloader.cs`，串行（一个下完再下一个）：

- **顺序执行**：内网带宽有限，串行已经能跑满八成，先要正确性
- **跳过已完成**：本地文件存在且大小和清单 `size` 一致 → 不发请求，标 `Skipped`。实机两遍验证：第一遍 14 个 / 807.8 MB / 37 秒；第二遍 0 下载、0 传输、**0.9 秒**
- **单个失败不中断整批**：一个 404 不该让剩下十几个文件干等着，成败逐条记进结果列表
- **取消只在开工前看**：已经下完的留在结果里，没轮到的一个都不碰；当前正在下的那个文件保留 `.part`

### 7.1 「完成」不能靠进度猜

`BatchDownloader` 提供**两个**回调：

| 回调 | 语义 |
| --- | --- |
| `IProgress<BatchProgress>` | 字节级进度，**只用来画进度条** |
| `IProgress<BatchItemResult>` | 每个文件**一收工就回调一次**，带最终结果 |

为什么必须分开：进度回调会被节流丢帧（UI 侧 100 ms 只重绘一次），**失败的文件也永远到不了 100%**，跳过的文件压根没有进度。真机上就出过这个 bug —— 界面某一行永远卡在 99.x%，因为「完成」被错误地实现成「进度到 100%」。

---

## 8. UI 线程模型与进度回调

WinForms 里跨线程更新控件会抛 `InvalidOperationException`。这里的处理是：

- **UI 侧用 `Progress<T>`**：它在 UI 线程上创建，`Report` 时会把回调排回 UI 线程（内部抓住 `SynchronizationContext`）—— 这是正解
- **`BatchDownloader` 内部刻意避开 `Progress<T>`**：`Progress<T>` 把回调**异步投递**出去，`DownloadAsync` 都返回了回调可能还没跑完，收集到的进度会残缺。所以内部用一个直接同步转发的 `IProgress` 实现

同一个类型，在两个地方一个该用、一个不该用 —— 判断依据是「这一层需要同步语义还是异步语义」。

**进度节流**：底层每写 64 KB 报一次进度，660 MB 就是一万次。UI 侧按行记时间戳，同一行 100 ms 内只重绘一次。

**关窗口**：`FormClosing` 里取消 token；所有回调入口都判 `IsDisposed` —— 后台任务还在跑时回调进来，控件可能已经没了。

---

## 9. 测试与真实验证

- **143 条 xUnit 测试**，逻辑层全覆盖，跑一次约 0.4 秒。`dotnet test` 即可
- 网络层用 `StubHttpMessageHandler`（`TestDoubles.cs`）替身，能精确模拟：正常 200、支持 Range 的 206、不理会 Range 的 200、断点超出的 416、半路断流、错误状态码
- 不用 Mock 框架，手写替身 —— 网络语义的自定义响应比 Mock 框架的表达力更强

### 9.1 单元测试的盲区：真实网络必须单独跑

这个工程有两个 bug 是**测试全绿、真跑才现形**的：

1. 进度条永远停在 99.6%
2. 速度显示在 2.6 ↔ 10.8 MB/s 之间乱跳

所以纪律是：**每次收尾都要跑一次真实网络下载，光绿不算完。**

造可控断点的手法（真实场景下内网 81 MB/s，Ctrl+C 根本按不中；下面 `ktdl` 的运行方式见 [1.0](#10-先搞清楚名字和怎么跑起来)）：

```bash
# 先把前 60 MiB 拉下来当断点，再用 -c 续传。断点大小精确、可反复重跑对照
curl -r 0-62914559 -o "<输出路径>.part" "<url>"
ktdl -c "<url>" "<输出路径>"
```

---

## 10. 已知缺口与没做的事

| 缺口 | 影响 | 备注 |
| --- | --- | --- |
| **远端变化检测不出来** | 没存 ETag，也没用 `If-Range`。远端文件换了内容但大小一样时，会被当成「已完成」跳过；大小不同则报错要求手动删 `.part` | 最该补的一个 |
| **清单没给 `size` 就判断不了完整性** | 会无条件重下 | 生成的清单永远带 `size`，所以只影响手写清单 |
| **CLI 还没有清单参数** | 批量下载目前只在 WinForms 里；命令行只能下一个文件 | 缺 `-m <清单地址>` 这类入口 |
| **串行下载** | 单线程约 21 MB/s，没喂满内网的 81 MB/s | 见储备 11.A.1 |
| **没有限速** | 会吃满带宽 | 见储备 11.A.2 |
| **没有自动重试** | 一个文件的网络抖动直接记为失败，需要用户再点一次 | 见储备 11.A.3 |
| **下载状态不持久化** | 关掉程序后不知道「哪些已经下完」——不过本地文件本身就是状态（靠 `size` 判断） | 清单驱动的方式天然不需要状态文件 |

---

## 11. 技术储备记录

> **这份记录的用途**：这个工程踩到的每个技术点，属于「现在够用，但值得往深挖一层」的清单。
> 每条按 **是什么 → 为什么值得储备 → 下一步怎么练** 写，方便以后照着推进，不用重新回忆上下文。

### A. 下载引擎本身

#### A.1 并发下载与多线程分段下载

- **是什么**：一次下 N 个文件（并发队列 + 限量）；或把一个文件切 N 段、每段一个 Range 请求并行下、最后合并。
- **为什么值得储备**：现在是串行 21 MB/s，实测链路能跑 81 MB/s —— **有四倍带宽躺在那里没用**。分段下载是断点续传的自然延伸（每段各自的 `.part`），是下载器的分水岭能力。
- **下一步怎么练**：先做「N 个文件并发」（`SemaphoreSlim` 限流 + 每文件独立进度），再做「单文件分段」（`RandomAccess.Write` 按偏移写入，避免 seek 竞争 + 预分配文件空间）。两者都要解决：合并失败的清理、总进度聚合、取消传播。

#### A.2 限速（令牌桶）

- **是什么**：控制单位时间放行的字节数。经典实现是令牌桶：按速率往桶里加令牌，读一块前先取够令牌，不够就等。
- **为什么值得储备**：下载器吃满带宽会把别人的业务拖垮；「能限速」是下载器进生产环境的门票。
- **下一步怎么练**：实现令牌桶，注意两个坑 —— (1) **不能 sleep 死等**，要用 `await Task.Delay` + `CancellationToken`，否则取消会卡住；(2) 桶的容量要允许一点突发（burst），否则速率曲线会变成锯齿。

#### A.3 重试与指数退避

- **是什么**：失败自动重试，间隔按指数增长（1s → 2s → 4s），并加随机抖动。
- **为什么值得储备**：现在一个瞬时 500 就记成失败，用户得手动再点，而续传能力本来让重试变得极便宜（接着 `.part` 下就行）。
- **下一步怎么练**：给 `HttpFileDownloader` 外面套一层重试装饰器（保持 `IFileDownloader` 接口不变 —— 这是抽象接口便宜的地方）。要区分**可重试**（5xx、超时、连接重置）和**不该重试**（404、401、路径非法），否则 404 会被重试到天荒地老。

#### A.4 完整性校验与远端变化检测

- **是什么**：`ETag` / `Last-Modified` + `If-Range` 请求头：续传时带上它，服务器能判断「文件是否变过」，变过就回 200 从头来。
- **为什么值得储备**：当前最大的正确性缺口 —— **只比长度，远端换了内容会被当成已完成跳过**。`If-Range` 是 HTTP 为「安全续传」专门设计的机制，属于必须知道的协议细节。
- **下一步怎么练**：把 `ETag` 存进清单，续传时带 `If-Range`，处理 200/206 两种回应。更强的一层是内容校验（SHA-256 / XxHash64 增量哈希），能防「内容损坏但长度相同」。

### B. 网络与 IO

#### B.1 `HttpClient` 的生命周期与连接层调优

- **是什么**：`HttpClient` 该单例复用（内部连接池）；`SocketsHttpHandler` 的 `PooledConnectionLifetime`、`MaxConnectionsPerServer`、超时、代理、压缩。
- **为什么值得储备**：现在 `HttpClient` 是单例但没有调过任何参数。并发一上来，`MaxConnectionsPerServer`（默认不限）和连接复用策略会直接决定吞吐；DNS 变更后不复用旧连接要靠 `PooledConnectionLifetime`。
- **下一步怎么练**：在 `SocketsHttpHandler` 上开 `MaxConnectionsPerServer`、设 `PooledConnectionLifetime`，用并发压测对比参数变化对吞吐的影响。顺便理解「为什么不是 `using var client = new HttpClient()` 就完事」。

#### B.2 大文件 IO 的细节

- **是什么**：缓冲大小、`FileOptions.SequentialScan`、预分配文件空间（`FileStream.SetLength`）、扇区对齐、磁盘写缓存。
- **为什么值得储备**：当前 64 KB 缓冲是拍出来的，没有实测过。缓冲大小与吞吐的关系是经验性知识，换个存储介质（机械盘 / 网络盘）结论就变。
- **下一步怎么练**：写一个基准小程序，把 64 KB / 256 KB / 1 MB 缓冲各自的吞吐跑出来。再试预分配：分段并发下载时如果不预分配，多线程写同一个文件的尾部会互相争抢。

#### B.3 `System.Text.Json` 的进阶用法

- **是什么**：源生成器（`JsonSerializerContext`）、`Utf8JsonWriter` 手写、自定义 `JsonNamingPolicy`、流式读写。
- **为什么值得储备**：当前用的是反射式序列化 + camelCase 命名策略（够用且好读）。但源生成器能免反射、对 AOT/剪裁友好、启动更快；超大清单流式读写能避免整份载入内存。
- **下一步怎么练**：给清单加 `JsonSerializerContext`，把 `PropertyNamingPolicy` 换成显式的 `[JsonPropertyName]`，对比启动耗时与剪裁后的体积。

#### B.4 HTTP/2、HTTP/3 与多路复用

- **是什么**：HTTP/2 在**一条 TCP 连接**上跑多个并发请求（多路复用），不会有 HTTP/1.1 的队头阻塞；HTTP/3 跑在 QUIC（UDP）上，弱网表现更好。
- **为什么值得储备**：并发下载方案里，「N 条连接各下一个文件」和「一条 HTTP/2 连接多路复用」是两条完全不同的路。IIS 对 HTTP/2 的支持与 `HttpClient` 的 `HttpVersion` 协商策略都值得亲手验一遍。
- **下一步怎么练**：把 `HttpRequestMessage.VersionPolicy` 设成 `RequestVersionOrHigher`，看实际协商到哪个版本，量一量并发时的差异。

### C. UI 与并发模型

#### C.1 WinForms 消息循环与 `async void`

- **是什么**：WinForms 的 `SynchronizationContext`、`async void` 事件处理器的异常传播、`IsDisposed` 竞态、`Invoke` vs `Progress<T>`。
- **为什么值得储备**：现在的写法能用，但 `async void` 里的异常如果没被 try/catch 兜住会**直接崩进程**；关窗时后台回调与控件生命周期是经典的竞态来源。这块属于「知道边界在哪，才敢改」的知识。
- **下一步怎么练**：查一遍 `Application.ThreadException` / `AppDomain.UnhandledException` 的兜底机制，验证「关窗瞬间回调进来」的路径；把 `Progress<T>` 在 UI 线程创建与在工作线程创建的差异用实验确认一遍。

#### C.2 结构化并发与取消传播

- **是什么**：`CancellationTokenSource` 的层级（`CreateLinkedTokenSource`）、取消的传播与清理、`Task.WhenAll` 的异常聚合、`IAsyncEnumerable` 做流式任务队列。
- **为什么值得储备**：批量下载加并发之后，「停止」要能一次收拢所有在途请求，且每个都保留 `.part`。取消语义在并发下会复杂一个量级。
- **下一步怎么练**：用 `CreateLinkedTokenSource` 把「批量取消」和「单文件超时」串起来，验证取消后没有孤儿任务、每个 `.part` 都完整。

#### C.3 批量任务的进度聚合

- **是什么**：总进度条怎么算 —— 按文件数加权，还是按字节数加权？
- **为什么值得储备**：现在是「按文件数」逐格推进（取消时看得出停在哪）。但一个 660 MB 加十三个小文件的批次，按文件数算的总进度会显得「一直不动然后突然走完」。按字节加权更贴合直觉，但清单没给 `size` 时算不出来 —— 是个有取舍的设计题。
- **下一步怎么练**：改成「有 size 就按字节加权、没有就退化到按文件数」，对比两种观感。

### D. 发布与工程化

#### D.1 打包发布与剪裁

- **是什么**：单文件发布（`PublishSingleFile`）、自包含（`SelfContained`）、`PublishTrimmed`、ReadyToRun、Native AOT。
- **为什么值得储备**：要给不懂 .NET 的人发一个「双击就能跑」的 exe。**关键约束：WinForms 不支持 Native AOT**（`PublishTrimmed` 对 WinForms 也有限制）—— 所以两个 WinForms 程序和纯库 `ktdl` 的发布策略必须分开考虑，这是个容易踩空的坑。
- **下一步怎么练**：给 `ktdl` 试 Native AOT（纯库、无 WinForms，可行性高，能压到几 MB 且冷启动极快），给两个 UI 用 `PublishSingleFile` + 自包含，量体积和启动时间。

#### D.2 清单的生成/消费契约自动化

- **是什么**：把「生成端和加载端必须一致」这件事，从「靠人记」变成「靠测试锁」。
- **为什么值得储备**：当前已经有 round-trip 测试兜着（生成 → 加载端解析 → 断言路径/大小/URL）。这是跨端契约测试的最小形态，值得作为习惯固化下来。
- **下一步怎么练**：清单格式若要加字段（如 `etag`、`hash`），先改 round-trip 测试再改两端实现，保证字段语义只有一处定义。

---

## 12. 踩过的坑（经验沉淀）

这些坑都是**单元测试全绿、真跑才现形**的，单独记下来避免重复支付学费：

1. **进度条永远停在 99.6%** —— 把「完成」实现成「进度显示 100%」。节流会丢帧，失败的文件永远到不了 100%，跳过的文件没有进度。**完成状态只能由独立的完成回调给。**
2. **速度显示乱跳（2.6 ↔ 10.8 MB/s）** —— 瞬时速度直接拿「相邻两次采样的字节差」算，受单次 64 KB 报块粒度和磁盘抖动影响极大。要用滑动窗口（工程里的 `SlidingSpeedMeter`）。
3. **`.part` 的存留策略翻过盘** —— 一开始取消就删 `.part`，后来改成一律保留。**取消的语义是「暂停」不是「放弃」**，删掉等于把用户已经付出的时间扔掉。
4. **`Path.Combine` 会静默丢掉根** —— 传进绝对路径时，前面的根会被无声地替换掉。任何「相对路径 → 本地路径」的映射都必须先拦绝对路径，再比对最终结果是否越界。
5. **清单条目是远端数据** —— 绝对 URL 决定请求打到哪台机器，`..` 决定文件落到哪个目录。这两样都不能由远端说了算。
6. **`.part` 落盘前必须释放文件句柄** —— 否则 `File.Move` 在 Windows 上撞 `IOException`。用作用域（花括号）控制 `await using` 的释放时机。
7. **`Progress<T>` 是异步投递的** —— 需要「回调在 `DownloadAsync` 返回前全部收齐」的场合不能用它，得用同步转发的 `IProgress` 实现。同一个类型在不同层该用、不该用，判断依据是同步还是异步语义。

---

## 参考

- [RFC 9110 §14 Range Requests](https://www.rfc-editor.org/rfc/rfc9110#section-14) —— `Range` / `Content-Range` / `If-Range` / 206 / 416 的权威定义
- [RFC 9110 §15.3.7 206 Partial Content](https://www.rfc-editor.org/rfc/rfc9110#section-15.3.7)
- [MDN: HTTP range requests](https://developer.mozilla.org/en-US/docs/Web/HTTP/Range_requests) —— 实践视角的中文可读版本
- [.NET 官方文档：HttpClient 指南](https://learn.microsoft.com/dotnet/fundamentals/networking/http/httpclient)
