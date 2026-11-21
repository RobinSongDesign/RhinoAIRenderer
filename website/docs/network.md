# 无法连接网络

插件生成图片、登录账号、查询余额都要连接 `https://api.rhchameleon.top`。连不上时，插件会显示下面两种提示之一：

| 提示 | 含义 |
|---|---|
| **Rhino 无法连接网络：这台电脑的防火墙或安全软件阻止了 Rhino 联网** | 请求没有离开你的电脑，是本机的设置拦下了 Rhino。按本页的 [Windows 防火墙](#windows-防火墙) 和 [安全软件](#安全软件) 两节处理 |
| **无法连接到服务商，请检查网络** | 请求发出去了但没有到达服务器：网络断开、代理或公司网络限制、hosts 文件改动等。从 [第一步](#第一步确认网络本身正常) 开始排查 |

点开提示旁的「详情」能看到系统的原始报错。若看到「以一种访问权限不允许的方式做了一个访问套接字的尝试」（错误代码 10013），就是第一种情况。

## 第一步：确认网络本身正常

用浏览器打开 <https://api.rhchameleon.top>。

- **打不开**：问题在网络本身，不在 Rhino。检查网络连接、代理和 VPN，见 [公司网络、代理与 VPN](#公司网络代理与-vpn)。
- **能打开，但插件连不上**：说明只有 Rhino 被拦住了，继续看下面几节。

## Windows 防火墙

Windows 防火墙可以单独禁止某个程序联网。要注意：**同一个程序只要有一条「阻止」规则，再添加「允许」规则也不起作用**，必须把阻止规则禁用或删除。

### 方法一：图形界面

1. 按 `Win + R`，输入 `wf.msc`，回车，打开「高级安全 Windows Defender 防火墙」。
2. 点左侧的 **出站规则**。
3. 在列表里找「程序」一列包含 `Rhino` 的规则（可以点「程序」列标题排序）。Rhino 8 的程序路径通常是 `C:\Program Files\Rhino 8\System\Rhino.exe`。
4. 「操作」为 **阻止**（图标是红色圆圈）的规则：右键 → **禁用规则**。
5. 再点左侧的 **入站规则**，同样检查一遍（插件不需要入站连接，但有的规则同时作用于两个方向）。
6. 回到 Rhino，重新打开 Chameleon 面板再试。不需要重启 Rhino。

### 方法二：PowerShell

在开始菜单搜索「PowerShell」，右键 **以管理员身份运行**，然后执行：

列出所有阻止 Rhino 的规则：

```powershell
Get-NetFirewallApplicationFilter | Where-Object { $_.Program -like '*Rhino*' } |
  Get-NetFirewallRule | Where-Object { $_.Action -eq 'Block' } |
  Format-Table DisplayName, Direction, Enabled
```

禁用其中的出站阻止规则：

```powershell
Get-NetFirewallApplicationFilter | Where-Object { $_.Program -like '*Rhino*' } |
  Get-NetFirewallRule | Where-Object { $_.Action -eq 'Block' -and $_.Direction -eq 'Outbound' } |
  Disable-NetFirewallRule
```

如果你的电脑默认禁止所有程序出站（公司电脑较常见），还需要允许 Rhino：

```powershell
New-NetFirewallRule -DisplayName "Rhino 8" -Direction Outbound -Action Allow `
  -Program "C:\Program Files\Rhino 8\System\Rhino.exe"
```

Rhino 装在其他位置时，把路径换成实际的 `Rhino.exe` 路径（在 Rhino 运行时，打开任务管理器 → 详细信息 → 右键 Rhino.exe → 打开文件所在的位置）。

## 安全软件

火绒、360 安全卫士、腾讯电脑管家、卡巴斯基、ESET 等软件都有自己的联网控制，和 Windows 防火墙是两套设置。在软件里找名为 **联网控制**、**网络防火墙**、**流量防火墙** 或 **程序联网管理** 的功能，在程序列表中找到 Rhino，改为 **允许联网**。

不同软件、不同版本的菜单名称不一样，找不到时可以暂时退出安全软件再试一次：如果退出后能连上，就确定是它拦截的。

## hosts 文件

hosts 文件可以把网址指向错误的地址，导致连接失败或超时。

1. 在开始菜单搜索「记事本」，右键 **以管理员身份运行**。
2. 打开 `C:\Windows\System32\drivers\etc\hosts`（文件类型选「所有文件」）。
3. 查找包含 `rhchameleon` 的行。找到就在行首加 `#` 或整行删除，然后保存。
4. 以管理员身份打开命令提示符，执行 `ipconfig /flushdns`。

macOS 的 hosts 文件在 `/etc/hosts`，用 `sudo nano /etc/hosts` 编辑，改完执行 `sudo dscacheutil -flushcache`。

## 公司网络、代理与 VPN

- 公司或学校网络可能限制访问外部网站，请联系网络管理员放行 `api.rhchameleon.top` 的 443 端口（HTTPS）。
- 插件使用系统的代理设置（Windows：设置 → 网络和 Internet → 代理）。浏览器能打开而插件不能时，检查浏览器是否单独配置了代理或插件。
- 开着 VPN 或加速器时，试着关掉再连；反过来，网络本身受限时也可以试着打开。

在 Windows 上，可以用 PowerShell 检查电脑能否连到服务器：

```powershell
Test-NetConnection api.rhchameleon.top -Port 443
```

结果中 `TcpTestSucceeded : True` 表示电脑本身能连上，问题出在针对 Rhino 的限制（防火墙或安全软件）；`False` 表示整台电脑都连不上，问题在网络、代理或 hosts。

## macOS

macOS 自带的防火墙只管理 **传入** 连接，不会阻止 Rhino 联网。若装了 Little Snitch、LuLu 等出站防火墙，在其规则列表里找到 Rhino 8，改为允许连接 `api.rhchameleon.top`。

## 仍然无法连接

请把以下信息发给我们：

1. 插件里的错误提示截图（点开「详情」）。
2. 浏览器能否打开 <https://api.rhchameleon.top>。
3. 上面 `Test-NetConnection` 的结果（Windows）。
4. 操作系统版本、Rhino 版本，以及是否安装了安全软件、是否在公司网络中。
