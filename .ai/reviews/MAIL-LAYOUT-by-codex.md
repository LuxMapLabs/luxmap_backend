# MAIL-LAYOUT — review giao diện bởi Codex

Codex CLI 0.166.0-alpha.5.1, `gpt-6.1-sol`, effort high, `--sandbox read-only`, 13/11/2026, ~49.5k token. Phạm vi: `AccountMailTemplate.cs` (commit 8963ccb) + HTML đã render.

**Kết quả xử lý (Claude):** cả 3 P2 đúng — tự tính lại tương phản khớp số (4.44, 2.64 → 5.14, 4.79). Đã sửa cả 3; P3 / dark mode: ghi nhận, không có cách sửa chỉ bằng inline.

---

- **P2 — Amber label narrowly fails AA.** [AccountMailTemplate.cs:90](src/LuxMap.Modules.Identity/Accounts/AccountMailTemplate.cs:90): `#a86a00` on white measures **4.44:1**, below the 4.5:1 requirement for 12px bold text. Use `color:#9c6000;` (**5.14:1**).

- **P2 — Footer contrast is too low.** [AccountMailTemplate.cs:112](src/LuxMap.Modules.Identity/Accounts/AccountMailTemplate.cs:112): `#8a96a8` on `#eef1f5` measures **2.64:1**. At 12px, both footer lines are difficult to read. Replace with `color:#5f6b7a;` (**4.79:1**).

- **P2 — Outlook Word loses internal spacing.** [AccountMailTemplate.cs:110](src/LuxMap.Modules.Identity/Accounts/AccountMailTemplate.cs:110): the paragraph’s `padding-top:18px` is unsupported; the safety note can crowd its divider. The eyebrow’s `padding-bottom:8px` at line 90 has the same problem. Move these gaps onto presentation-table cells, retaining typography on the text elements:
  ```html
  <tr><td style="border-top:1px solid #e3e8ef;padding-top:18px;">
    <p style="margin:0;font-size:13px;line-height:20px;color:#5f6b7a;">…</p>
  </td></tr>
  ```
  [Padding support](https://www.caniemail.com/features/css-padding/).

**Fine:** hierarchy, restrained colours, body typography, and the prominent 48px-high CTA work well. Body text (**5.43:1**) and button text (**14.63:1**) pass AA. Vietnamese copy is clear and appropriately reassuring; the invite/reset distinction is useful. Omitting the HTML fallback URL matches your chosen constraint.

**375px:** calculated body width is approximately **277px**; the supplied short username fits, although the invite heading will wrap. This is a layout assessment, not an observed device render.

**Compatibility limitation:** Gmail web/app and Apple Mail suit this table/inline approach. Outlook’s square corners are acceptable degradation. `color-scheme:light` does **not universally prevent dark-mode inversion**; Gmail iOS and Outlook Windows remain client-dependent, with no universal inline-only fix. [Dark-mode behaviour](https://www.litmus.com/blog/the-ultimate-guide-to-dark-mode-for-email-marketers).

✅ Contrast calculated; specified diff and HTML inspected. ⚠️ No live client/dark-mode rendering verified. No builds or tests run; no confirmed P1.