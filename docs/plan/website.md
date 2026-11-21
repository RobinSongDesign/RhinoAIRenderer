# 产品网站 rhchameleon.top

`rhchameleon.top` 是 Chameleon 的产品网站：介绍页与使用文档。账号、充值、令牌与 API 在子域名 `api.rhchameleon.top`（new-api，见 [`relay-gateway.md`](relay-gateway.md)）。插件内置的「Chameleon 账号」服务商指向 `https://api.rhchameleon.top`；插件中「查看解决方法」等链接指向 `https://rhchameleon.top/docs/...`（地址集中在 `src/Chameleon.UI/src/help/helpLinks.ts`）。

| 地址 | 内容 | 来源 |
|---|---|---|
| `rhchameleon.top` | 介绍页 | `website/index.html` |
| `rhchameleon.top/docs/<页面>` | 文档，由 `website/docs/index.html` 在浏览器中渲染同名 `.md` | `website/docs/*.md` |
| `www.rhchameleon.top` | 跳转到 `rhchameleon.top` | Nginx |
| `api.rhchameleon.top` | new-api：注册、登录、钱包、令牌、价格、生成接口 | 已有的 new-api 容器 |
| `api.robinsong.top` | 同上，旧域名，保留给旧版插件 | 已有的 Nginx 站点 |

## 1. 中英文

- 整站中英双语：首页、导航、页脚用 `.l-zh` / `.l-en` 标记两种语言的文字；`website/assets/lang.js` 在页面绘制前给 `<html>` 设置 `data-lang`，`site.css` 只显示当前语言。
- 默认语言按顺序决定：地址里的 `?lang=zh|en`（插件的「查看解决方法」会带上插件的界面语言）→ 访客在本站的选择（存在浏览器的 localStorage）→ 系统/浏览器语言（中文为 `zh`，英文为 `en`，其他语言显示英文）。右上角「English / 中文」切换并记住。
- 文档：中文 `<slug>.md`，英文 `<slug>.en.md`，两份内容保持一致；切换语言时阅读器重新加载对应文件。新增文档时两种语言一起写，并在 `docs.js` 的 `PAGES` 中登记两种标题。
- 关于、用户协议、隐私政策的 HTML 同时包含中英文（供 new-api 使用），网站按当前语言只显示其中一种。

## 1.0 文档的写法

- 文档是 `website/docs/` 下的 Markdown 文件，文件名即地址（`network.md` → `/docs/network`）。新增页面后，在 `website/assets/docs.js` 的 `PAGES` 中登记标题和顺序。
- 标题自动生成锚点：小写，空格换成 `-`，去掉标点，例如 `## Windows 防火墙` → `#windows-防火墙`。
- 插件链接到的页面（目前是 `/docs/network`）改名或删除前，先改 `helpLinks.ts` 并发布新版插件。
- Markdown 由内置的 `website/assets/vendor/marked-18.1.3.umd.js`（MIT，许可证在同目录）渲染，不依赖外部 CDN，国内访问不受影响。
- 本地预览：用 `deploy/website/nginx-rhchameleon.conf`（把 `root` 改成本地路径）启动 Nginx。直接用 `python -m http.server` 时 `/docs/network` 这类地址无法访问，只能看首页。

## 1.1 品牌与条款

- 徽标在 `website/assets/brand/`：`logo.svg`（青到洋红渐变圆底，白色变色龙，用作图标、头像、网站图标）、`logo-mark.svg`（渐变变色龙，透明底，用于浅色背景上的大尺寸展示），以及 512 / 192 / 64 / 32 像素的 PNG 和 `website/favicon.ico`。渐变为 `#00C8E0` → `#E6007E`；网页正文强调色在浅色模式为 `#b0006b`、深色模式为 `#4fd6ea`，以保证可读性。
- 徽标由用户提供的原始变色龙图标描摹为矢量后重新着色，眼睛按原图重画。修改时直接编辑 SVG，再用浏览器导出各尺寸 PNG。
- new-api 后台：「徽标 URL」填 `https://rhchameleon.top/assets/brand/logo-192.png`。

## 1.2 关于、用户协议、隐私政策

- 三份文档都是英文在前、中文在后，不做语言切换。之前试过多语言加 CSS 标签切换，在 new-api 里会和导航栏重叠，按钮也不好用，所以改成现在这样。
- 原文是 `website/legal/src/<文档>.<en|zh-CN>.md`。支持的 Markdown 写法：标题、段落、列表、表格、粗体、链接，以及以 `~ ` 开头的说明行。
- 运行 `python3 website/legal/build.py`，生成 `website/legal/{about,terms,privacy}.html`，只用 Python 标准库。CI 会用 `--check` 确认生成结果和原文一致。
- 运营者名称、联系邮箱、生效日期写在 `website/legal/legal.json`。
- new-api（`v1.0.0-rc.51`）显示这三栏的方式，以及我们的对策：
  - HTML 放在隔离的 Shadow DOM 里，脚本会被删掉。
  - 页面没有外层容器，内容紧贴在 3.5rem 高的固定导航栏下面，所以片段顶部留出了 `3.5rem + 32px` 的空白。
  - new-api 会把自己的样式表（包括 Tailwind 的样式重置）复制进隔离区域，所以片段里写死了列表符号、标题字重等样式。
  - 文字颜色跟随 new-api 的亮色、暗色主题。
- 网站上的 `/docs/about`、`/docs/terms`、`/docs/privacy` 显示同一份 HTML，并去掉了为导航栏预留的空白。
- 改了原文或 `legal.json` 后：运行 `build.py`，提交；服务器上 `git pull`；再把新的 HTML 重新粘贴到 new-api 后台对应的三栏。
- 这些文本不是法律意见，正式收费前建议请律师审阅。中英文如有不一致，以中文版为准。

## 2. 部署（东京服务器，2026-11）

服务器已有 Nginx 与 certbot。以下命令都需要 `sudo`。

### 2.1 DNS

在域名注册商处添加三条 A 记录，指向服务器公网 IP：`@`、`www`、`api`。用 `ping rhchameleon.top` 确认已解析到服务器后再继续。

### 2.2 网站

```bash
sudo git clone --depth 1 -b claude/rhinoairender-dev-plan-lkoak6 \
  https://github.com/RobinSongDesign/RhinoAIRenderer.git /opt/rhchameleon-site
sudo curl -fsSL -o /etc/nginx/sites-available/rhchameleon.conf \
  https://raw.githubusercontent.com/RobinSongDesign/RhinoAIRenderer/claude/rhinoairender-dev-plan-lkoak6/deploy/website/nginx-rhchameleon.conf
sudo ln -s /etc/nginx/sites-available/rhchameleon.conf /etc/nginx/sites-enabled/
sudo nginx -t && sudo systemctl reload nginx
sudo certbot --nginx -d rhchameleon.top -d www.rhchameleon.top
```

更新网站：`sudo git -C /opt/rhchameleon-site pull`，无需重启 Nginx。仓库合并到主分支后，把克隆命令中的分支改为主分支。

### 2.3 账号与 API 子域名

新建一个站点，内容与旧域名的站点相同（反向代理到 `127.0.0.1:3000`），只改域名：

```bash
sudo sed 's/api.example.com/api.rhchameleon.top/' /opt/rhchameleon-site/deploy/relay/nginx-relay.conf \
  | sudo tee /etc/nginx/sites-available/api-rhchameleon.conf > /dev/null
sudo ln -s /etc/nginx/sites-available/api-rhchameleon.conf /etc/nginx/sites-enabled/
sudo nginx -t && sudo systemctl reload nginx
sudo certbot --nginx -d api.rhchameleon.top
```

旧域名 `api.robinsong.top` 的站点保持不动，已经安装的旧版插件继续可用。

然后在 new-api 后台「系统设置」中把 **服务器地址** 改为 `https://api.rhchameleon.top`（找回密码邮件、支付回调等链接使用它）。

### 2.4 验收

- 浏览器打开 `https://rhchameleon.top`、`https://rhchameleon.top/docs/network`、`https://www.rhchameleon.top`（应跳转）、`https://api.rhchameleon.top`。
- 新版插件用账号登录、生成一张图、查看余额。
