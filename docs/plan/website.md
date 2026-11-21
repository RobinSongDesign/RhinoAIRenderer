# 产品网站 rhchameleon.top

`rhchameleon.top` 是 Chameleon 的产品网站：介绍页与使用文档。账号、充值、令牌与 API 在子域名 `api.rhchameleon.top`（new-api，见 [`relay-gateway.md`](relay-gateway.md)）。插件内置的「Chameleon 账号」服务商指向 `https://api.rhchameleon.top`；插件中「查看解决方法」等链接指向 `https://rhchameleon.top/docs/...`（地址集中在 `src/Chameleon.UI/src/help/helpLinks.ts`）。

| 地址 | 内容 | 来源 |
|---|---|---|
| `rhchameleon.top` | 介绍页 | `website/index.html` |
| `rhchameleon.top/docs/<页面>` | 文档，由 `website/docs/index.html` 在浏览器中渲染同名 `.md` | `website/docs/*.md` |
| `www.rhchameleon.top` | 跳转到 `rhchameleon.top` | Nginx |
| `api.rhchameleon.top` | new-api：注册、登录、钱包、令牌、价格、生成接口 | 已有的 new-api 容器 |
| `api.robinsong.top` | 同上，旧域名，保留给旧版插件 | 已有的 Nginx 站点 |

## 1. 文档的写法

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

- 三份文档提供 new-api 的全部 7 种界面语言：简体中文、繁体中文、英语、日语、法语、俄语、越南语。原文是 `website/legal/src/<文档>.<语言>.md`（Markdown 子集：标题、段落、列表、表格、粗体、链接、`~ ` 开头的说明行）。繁体中文由 `build.py` 用 OpenCC（`s2twp`，台湾用词）从简体转换，不单独维护。
- `python3 website/legal/build.py` 生成 `website/legal/{about,terms,privacy}.html`。每份都是独立的 HTML 片段，包含全部语言，顶部用纯 CSS 的语言标签切换（单选框 + `:checked`）。之所以不用脚本，是因为 new-api 会删除这些设置里的脚本，也不会把界面语言写到页面的 `lang` 属性上。生成需要 `pip install opencc-python-reimplemented`；CI 用 `--check` 确认生成结果与原文一致。
- 运营者名称、联系邮箱、生效日期写在 `website/legal/legal.json`，留空时显示各语言的占位符。
- new-api 后台「关于」「用户协议」「隐私政策」三栏分别粘贴对应 HTML 文件的全文。已用 new-api `v1.0.0-rc.51` 的 DOMPurify 3.4.17 与相同的清理参数实测：用户协议、隐私政策（隔离显示）完整保留；「关于」（直接嵌入）只去掉链接的 `target` 属性，链接改为在当前页打开。语言切换在亮色、暗色主题下都正常；文字颜色继承 new-api 主题，不写死。
- 网站上的 `/docs/about`、`/docs/terms`、`/docs/privacy` 显示同一份 HTML，并按 `?lang=` 或浏览器语言自动选中标签（不支持的语言显示简体中文）。
- 改了原文或 `legal.json` 后：运行 `build.py`，提交，服务器上 `git pull`，并把新的 HTML 重新粘贴到 new-api 后台。
- 这些文本不是法律意见，正式收费前建议请律师审阅。各语言版本声明以简体中文版为准。

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
