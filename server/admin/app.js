/* ══ Durango LastHuman Admin Tool — app.js ═══════════════════════════════════════ */

(function () {
    'use strict';

    // ── State ──────────────────────────────────────────────────────────────────
    let serverUrl = '';
    let adminToken = '';
    let configData = null;
    let configMeta = null;
    let islandsData = null;
    let refreshTimer = null;

    // ── DOM Refs ───────────────────────────────────────────────────────────────
    const $ = (sel) => document.querySelector(sel);
    const $$ = (sel) => document.querySelectorAll(sel);

    // Auto-detect server URL จากหน้าเว็บปัจจุบัน
    $('#server-url').value = window.location.origin;

    // ── API Helper ─────────────────────────────────────────────────────────────
    async function api(method, path, body) {
        // ส่ง token ผ่าน query param เพื่อความเข้ากันได้ (header อาจถูก block โดย browser)
        const sep = path.includes('?') ? '&' : '?';
        const url = serverUrl + path + sep + 'token=' + encodeURIComponent(adminToken);
        const headers = {};
        const opts = { method, headers };
        if (body) {
            if (typeof body === 'string') {
                headers['Content-Type'] = 'application/x-www-form-urlencoded';
                opts.body = body;
            } else {
                headers['Content-Type'] = 'application/x-www-form-urlencoded';
                const params = new URLSearchParams();
                for (const [k, v] of Object.entries(body)) {
                    params.append(k, v);
                }
                opts.body = params.toString();
            }
        }
        const resp = await fetch(url, opts);
        const text = await resp.text();
        try { return JSON.parse(text); } catch { return { _raw: text }; }
    }

    // ── Login ──────────────────────────────────────────────────────────────────
    $('#login-form').addEventListener('submit', async (e) => {
        e.preventDefault();
        serverUrl = $('#server-url').value.replace(/\/+$/, '');
        adminToken = $('#admin-token').value;
        $('#login-error').textContent = '';
        try {
            const health = await api('GET', '/health');
            if (health.error || health._raw?.includes('403')) {
                $('#login-error').textContent = 'Token ไม่ถูกต้องหรือเซิร์ฟไม่ตอบ';
                return;
            }
            $('#login-screen').classList.remove('active');
            $('#app-screen').classList.add('active');
            loadDashboard();
        } catch (err) {
            $('#login-error').textContent = 'เชื่อมต่อเซิร์ฟไม่ได้: ' + err.message;
        }
    });

    // ── Navigation ─────────────────────────────────────────────────────────────
    $$('.nav-item').forEach((item) => {
        item.addEventListener('click', () => {
            $$('.nav-item').forEach((n) => n.classList.remove('active'));
            $$('.page').forEach((p) => p.classList.remove('active'));
            item.classList.add('active');
            const page = item.dataset.page;
            $(`#page-${page}`).classList.add('active');
            if (page === 'dashboard') loadDashboard();
            else if (page === 'config') loadConfig();
            else if (page === 'features') loadConfig();
            else if (page === 'islands') loadIslands();
            else if (page === 'players') loadOnlinePlayers();
            else if (page === 'economy') loadEconomy();
            else if (page === 'actions') { /* static */ }
        });
    });

    // ══ เศรษฐกิจ — เฝ้าเงินเฟ้อ ═════════════════════════════════════════════════
    // เซิร์ฟใช้สกุลเดียวคือ T Stone (server/Core/Player.Wallet.cs)
    // ตัวเลข ณ จุดเดียวบอกอะไรไม่ได้ ต้องดูส่วนต่างตามเวลา ⇒ จำค่าครั้งก่อนไว้เทียบให้
    let lastEconomy = null;

    const fmt = (n) => Number(n || 0).toLocaleString('th-TH');

    async function loadEconomy() {
        const box = $('#eco-top');
        try {
            const d = await api('GET', '/admin/economy');
            $('#eco-total').textContent = fmt(d.total_tstone);
            $('#eco-chars').textContent = fmt(d.character_count);
            $('#eco-holders').textContent = fmt(d.holder_count);
            $('#eco-avg').textContent = fmt(d.average);
            $('#eco-median').textContent = fmt(d.median);
            $('#eco-max').textContent = fmt(d.max);

            // ส่วนต่างจากครั้งก่อน — บวกเร็วกว่าจำนวนคนโต = ก๊อกแรงกว่าท่อระบาย
            const now = Date.now();
            if (lastEconomy) {
                const dt = Math.max(1, (now - lastEconomy.at) / 1000);
                const dTotal = d.total_tstone - lastEconomy.total;
                const dChars = d.character_count - lastEconomy.chars;
                const perHour = Math.round((dTotal / dt) * 3600);
                const cls = dTotal > 0 ? 'error-text' : '';
                $('#eco-delta').innerHTML =
                    '<div class="' + cls + '">' +
                    'เงินรวมเปลี่ยน <b>' + (dTotal >= 0 ? '+' : '') + fmt(dTotal) + '</b> ' +
                    'ใน ' + Math.round(dt) + ' วินาที (≈ ' + (perHour >= 0 ? '+' : '') + fmt(perHour) + ' /ชม.)<br>' +
                    'ตัวละครเปลี่ยน ' + (dChars >= 0 ? '+' : '') + fmt(dChars) +
                    '</div>';
            }
            lastEconomy = { at: now, total: d.total_tstone, chars: d.character_count };

            const b = d.buckets || {};
            $('#eco-buckets').innerHTML = '<table class="data-table"><thead><tr><th>ช่วงยอดเงิน</th><th>จำนวนคน</th></tr></thead><tbody>' +
                Object.keys(b).map((k) => '<tr><td>' + k + '</td><td>' + fmt(b[k]) + '</td></tr>').join('') +
                '</tbody></table>';

            const rows = d.top || [];
            box.innerHTML = rows.length === 0
                ? 'ยังไม่มีตัวละครที่มีเงิน'
                : '<table class="data-table"><thead><tr><th>ชื่อ</th><th>T Stone</th><th>ออนไลน์</th></tr></thead><tbody>' +
                  rows.map((r) => '<tr><td>' + (r.name || r.entity_id) + '</td><td>' + fmt(r.t_stone) + '</td><td>' +
                                  (r.online ? '●' : '') + '</td></tr>').join('') +
                  '</tbody></table>';
        } catch (e) {
            box.textContent = 'โหลดไม่สำเร็จ: ' + e.message;
        }
    }

    $('#btn-refresh-economy').addEventListener('click', loadEconomy);

    // ── Logout ─────────────────────────────────────────────────────────────────
    $('#btn-logout').addEventListener('click', () => {
        clearInterval(refreshTimer);
        $('#app-screen').classList.remove('active');
        $('#login-screen').classList.add('active');
        serverUrl = '';
        adminToken = '';
    });

    // ══ Dashboard ══════════════════════════════════════════════════════════════

    async function loadDashboard() {
        try {
            const [health, who] = await Promise.all([
                api('GET', '/health'),
                api('GET', '/admin/who')
            ]);

            if (health.error) {
                $('#stat-players').textContent = '⚠️';
                return;
            }

            // Stats
            $('#stat-players').textContent = health.players_online ?? '—';
            $('#stat-max-players').textContent = health.max_players ?? '—';
            $('#stat-uptime').textContent = formatUptime(health.uptime_sec);
            $('#stat-tick').textContent = health.tick_ms ? `${health.tick_ms.p50?.toFixed(1)}ms` : '—';
            $('#stat-worlds').textContent = health.worlds_loaded ?? '—';
            $('#stat-save').textContent = health.last_save_ago_sec != null ? `${Math.round(health.last_save_ago_sec)}s ago` : '—';
            $('#stat-save-fail').textContent = health.save_failures ?? '0';
            $('#stat-errors').textContent = health.loop_errors ?? '0';

            // Online players
            const players = who.players || who;
            if (Array.isArray(players) && players.length > 0) {
                let html = '<table><tr><th>Name</th><th>Entity ID</th><th>Level</th><th>Region</th></tr>';
                for (const p of players) {
                    html += `<tr><td>${esc(p.name || p.Name || '—')}</td><td><code>${esc(p.entity_id || p.EntityId || '—')}</code></td><td>${p.level || p.Level || '—'}</td><td>${esc(p.region || p.Region || '—')}</td></tr>`;
                }
                html += '</table>';
                $('#online-players-list').innerHTML = html;
            } else {
                $('#online-players-list').innerHTML = '<div class="empty-state"><div class="icon">👤</div>ไม่มีผู้เล่นออนไลน์</div>';
            }

            // Unhandled packets
            const unhandled = health.unhandled_packet_types || {};
            const keys = Object.keys(unhandled);
            if (keys.length > 0) {
                let html = '<table><tr><th>TypeCode</th><th>Count</th></tr>';
                for (const k of keys.sort((a, b) => unhandled[b] - unhandled[a])) {
                    html += `<tr><td>${k}</td><td>${unhandled[k]}</td></tr>`;
                }
                html += '</table>';
                $('#unhandled-packets').innerHTML = html;
            } else {
                $('#unhandled-packets').textContent = 'ไม่มี — ทุก packet type มี handler แล้ว ✓';
            }
        } catch (err) {
            console.error('Dashboard load error:', err);
        }
    }

    // Auto-refresh dashboard
    refreshTimer = setInterval(() => {
        if ($('#page-dashboard').classList.contains('active')) loadDashboard();
    }, 5000);

    // ══ Config Editor ═══════════════════════════════════════════════════════════

    async function loadConfig() {
        try {
            const [cfg, meta] = await Promise.all([
                api('GET', '/admin/config'),
                api('GET', '/admin/config/meta')
            ]);
            if (cfg.error) { alert('ไม่สามารถโหลด config: ' + cfg.error); return; }
            configData = cfg;
            configMeta = meta.error ? null : meta;
            renderConfigEditor();
            renderFeatureToggles();
        } catch (err) {
            console.error('Config load error:', err);
        }
    }

    function renderConfigEditor() {
        if (!configData) return;
        const container = $('#config-sections');
        container.innerHTML = '';

        const skipSections = ['Features', 'Spawn', 'Zones', 'Starter'];

        for (const [section, data] of Object.entries(configData)) {
            if (skipSections.includes(section)) continue;

            const div = document.createElement('div');
            div.className = 'config-section';

            const header = document.createElement('div');
            header.className = 'config-section-header';
            header.innerHTML = `<span>▸ ${esc(section)}</span><span class="arrow">▸</span>`;
            header.addEventListener('click', () => div.classList.toggle('open'));
            div.appendChild(header);

            const body = document.createElement('div');
            body.className = 'config-section-body';

            if (typeof data === 'object' && data !== null && !Array.isArray(data)) {
                for (const [key, value] of Object.entries(data)) {
                    body.appendChild(createConfigField(section, key, value, configMeta?.[section]?.[key]));
                }
            } else if (Array.isArray(data)) {
                const ta = document.createElement('textarea');
                ta.className = 'code-editor';
                ta.rows = Math.min(8, data.length + 2);
                ta.value = JSON.stringify(data, null, 2);
                ta.dataset.section = section;
                ta.dataset.key = '__array__';
                ta.addEventListener('change', () => {
                    try {
                        configData[section] = JSON.parse(ta.value);
                    } catch (e) {
                        alert('JSON ผิดรูปแบบ: ' + e.message);
                    }
                });
                body.appendChild(ta);
            } else {
                body.appendChild(createConfigField(section, section, data, configMeta?.[section]));
            }

            div.appendChild(body);
            container.appendChild(div);
        }

        // Spawn section
        if (configData.Spawn) {
            const div = document.createElement('div');
            div.className = 'config-section';
            div.innerHTML = `<div class="config-section-header"><span>▸ Spawn (สัตว์)</span><span class="arrow">▸</span></div>`;
            div.querySelector('.config-section-header').addEventListener('click', () => div.classList.toggle('open'));
            const body = document.createElement('div');
            body.className = 'config-section-body';
            const ta = document.createElement('textarea');
            ta.className = 'code-editor';
            ta.rows = 12;
            ta.value = JSON.stringify(configData.Spawn, null, 2);
            ta.addEventListener('change', () => {
                try { configData.Spawn = JSON.parse(ta.value); } catch (e) { alert('JSON ผิดรูปแบบ: ' + e.message); }
            });
            body.appendChild(ta);
            div.appendChild(body);
            container.appendChild(div);
        }

        // Zones section
        if (configData.Zones) {
            const div = document.createElement('div');
            div.className = 'config-section';
            div.innerHTML = `<div class="config-section-header"><span>▸ Zones</span><span class="arrow">▸</span></div>`;
            div.querySelector('.config-section-header').addEventListener('click', () => div.classList.toggle('open'));
            const body = document.createElement('div');
            body.className = 'config-section-body';
            const ta = document.createElement('textarea');
            ta.className = 'code-editor';
            ta.rows = 10;
            ta.value = JSON.stringify(configData.Zones, null, 2);
            ta.addEventListener('change', () => {
                try { configData.Zones = JSON.parse(ta.value); } catch (e) { alert('JSON ผิดรูปแบบ: ' + e.message); }
            });
            body.appendChild(ta);
            div.appendChild(body);
            container.appendChild(div);
        }

        // Starter section
        if (configData.Starter) {
            const div = document.createElement('div');
            div.className = 'config-section';
            div.innerHTML = `<div class="config-section-header"><span>▸ Starter (ของเริ่มต้น)</span><span class="arrow">▸</span></div>`;
            div.querySelector('.config-section-header').addEventListener('click', () => div.classList.toggle('open'));
            const body = document.createElement('div');
            body.className = 'config-section-body';
            const ta = document.createElement('textarea');
            ta.className = 'code-editor';
            ta.rows = 10;
            ta.value = JSON.stringify(configData.Starter, null, 2);
            ta.addEventListener('change', () => {
                try { configData.Starter = JSON.parse(ta.value); } catch (e) { alert('JSON ผิดรูปแบบ: ' + e.message); }
            });
            body.appendChild(ta);
            div.appendChild(body);
            container.appendChild(div);
        }
    }

    function createConfigField(section, key, value, meta) {
        const row = document.createElement('div');
        row.className = 'config-field';

        const labelDiv = document.createElement('div');
        labelDiv.innerHTML = `<div class="config-field-label">${esc(key)}</div>`;
        if (meta?.d) {
            labelDiv.innerHTML += `<div class="config-field-desc">${esc(meta.d)}</div>`;
        }
        row.appendChild(labelDiv);

        const inputDiv = document.createElement('div');
        const input = document.createElement('input');

        if (typeof value === 'boolean') {
            input.type = 'checkbox';
            input.checked = value;
            input.dataset.section = section;
            input.dataset.key = key;
            input.dataset.type = 'bool';
            input.addEventListener('change', () => {
                configData[section][key] = input.checked;
            });
        } else if (typeof value === 'number') {
            input.type = Number.isInteger(value) ? 'number' : 'number';
            input.step = Number.isInteger(value) ? '1' : 'any';
            input.value = value;
            input.dataset.section = section;
            input.dataset.key = key;
            input.dataset.type = 'number';
            input.addEventListener('change', () => {
                configData[section][key] = Number(input.value);
            });
        } else {
            input.type = 'text';
            input.value = value ?? '';
            input.dataset.section = section;
            input.dataset.key = key;
            input.dataset.type = 'string';
            input.addEventListener('change', () => {
                configData[section][key] = input.value;
            });
        }

        inputDiv.appendChild(input);
        row.appendChild(inputDiv);
        return row;
    }

    // Save Config
    $('#btn-save-config').addEventListener('click', async () => {
        const status = $('#config-status');
        status.textContent = 'กำลังบันทึก...';
        status.className = 'status-text';
        try {
            const result = await api('POST', '/admin/config', { json: JSON.stringify(configData, null, 2) });
            if (result.saved) {
                status.textContent = '✓ บันทึกสำเร็จ';
                status.className = 'status-text success';
            } else {
                status.textContent = '❌ ' + (result.error || 'ไม่ทราบสาเหตุ');
                status.className = 'status-text error';
            }
        } catch (err) {
            status.textContent = '❌ ' + err.message;
            status.className = 'status-text error';
        }
    });

    // Reload Config
    $('#btn-reload-config').addEventListener('click', async () => {
        await loadConfig();
        $('#config-status').textContent = '✓ Reloaded';
        $('#config-status').className = 'status-text success';
    });

    // ══ Feature Toggles ════════════════════════════════════════════════════════

    function renderFeatureToggles() {
        if (!configData?.Features) return;
        const grid = $('#features-grid');
        grid.innerHTML = '';

        for (const [key, value] of Object.entries(configData.Features)) {
            const item = document.createElement('div');
            item.className = 'feature-toggle';

            const label = document.createElement('label');
            label.textContent = key;

            const switchDiv = document.createElement('label');
            switchDiv.className = 'toggle-switch';

            const input = document.createElement('input');
            input.type = 'checkbox';
            input.checked = !!value;
            input.addEventListener('change', () => {
                configData.Features[key] = input.checked;
            });

            const slider = document.createElement('span');
            slider.className = 'toggle-slider';

            switchDiv.appendChild(input);
            switchDiv.appendChild(slider);
            item.appendChild(label);
            item.appendChild(switchDiv);
            grid.appendChild(item);
        }
    }

    // Save Features
    $('#btn-save-features').addEventListener('click', async () => {
        const status = $('#features-status');
        status.textContent = 'กำลังบันทึก...';
        status.className = 'status-text';
        try {
            const result = await api('POST', '/admin/config', { json: JSON.stringify(configData, null, 2) });
            if (result.saved) {
                status.textContent = '✓ บันทึกสำเร็จ';
                status.className = 'status-text success';
            } else {
                status.textContent = '❌ ' + (result.error || 'ไม่ทราบสาเหตุ');
                status.className = 'status-text error';
            }
        } catch (err) {
            status.textContent = '❌ ' + err.message;
            status.className = 'status-text error';
        }
    });

    // ══ Islands ═════════════════════════════════════════════════════════════════

    async function loadIslands() {
        try {
            const data = await api('GET', '/admin/islands');
            if (data.error) { alert(data.error); return; }
            islandsData = data;
            renderIslands();
        } catch (err) {
            console.error('Islands load error:', err);
        }
    }

    function renderIslands() {
        if (!islandsData) return;
        const list = $('#islands-list');
        list.innerHTML = '';

        const islands = islandsData.Islands || [];
        for (const island of islands) {
            const card = document.createElement('div');
            card.className = 'card';
            card.innerHTML = `
                <h3>🏝️ ${esc(island.Name)} <code style="color:var(--text-dim);font-size:0.8em;">${esc(island.Id)}</code></h3>
                <div style="display:grid;grid-template-columns:repeat(auto-fill,minmax(180px,1fr));gap:12px;margin-top:12px;">
                    <div><label class="config-field-label">Name</label><input type="text" data-field="Name" value="${esc(island.Name)}"></div>
                    <div><label class="config-field-label">Terrain</label><input type="text" data-field="Terrain" value="${esc(island.Terrain)}"></div>
                    <div><label class="config-field-label">Min Level</label><input type="number" data-field="MinLevel" value="${island.MinLevel}"></div>
                    <div><label class="config-field-label">Max Level</label><input type="number" data-field="MaxLevel" value="${island.MaxLevel}"></div>
                    <div><label class="config-field-label">Required Level</label><input type="number" data-field="RequiredLevel" value="${island.RequiredLevel}"></div>
                    <div><label class="config-field-label">Host</label><input type="text" data-field="Host" value="${esc(island.Host)}"></div>
                    <div><label class="config-field-label">Gateway Port</label><input type="number" data-field="GatewayPort" value="${island.GatewayPort}"></div>
                    <div><label class="config-field-label">Game Port</label><input type="number" data-field="GamePort" value="${island.GamePort}"></div>
                </div>
            `;

            // Bind input changes
            card.querySelectorAll('input').forEach((input) => {
                input.addEventListener('change', () => {
                    const field = input.dataset.field;
                    const val = input.type === 'number' ? Number(input.value) : input.value;
                    island[field] = val;
                });
            });

            list.appendChild(card);
        }
    }

    // Save Islands
    $('#btn-save-islands').addEventListener('click', async () => {
        const status = $('#islands-status');
        status.textContent = 'กำลังบันทึก...';
        status.className = 'status-text';
        try {
            const result = await api('POST', '/admin/islands', { json: JSON.stringify(islandsData, null, 2) });
            if (result.saved) {
                status.textContent = '✓ บันทึกสำเร็จ';
                status.className = 'status-text success';
            } else {
                status.textContent = '❌ ' + (result.error || 'ไม่ทราบสาเหตุ');
                status.className = 'status-text error';
            }
        } catch (err) {
            status.textContent = '❌ ' + err.message;
            status.className = 'status-text error';
        }
    });

    // Reload Islands
    $('#btn-reload-islands').addEventListener('click', loadIslands);

    // ══ Players ═════════════════════════════════════════════════════════════════

    // Tabs
    $$('.tab-btn').forEach((btn) => {
        btn.addEventListener('click', () => {
            $$('.tab-btn').forEach((b) => b.classList.remove('active'));
            $$('.tab-content').forEach((c) => c.classList.remove('active'));
            btn.classList.add('active');
            $(`#tab-${btn.dataset.tab}`).classList.add('active');
            if (btn.dataset.tab === 'online') loadOnlinePlayers();
            else if (btn.dataset.tab === 'whitelist') loadWhitelist();
            else if (btn.dataset.tab === 'bans') loadBans();
        });
    });

    async function loadOnlinePlayers() {
        try {
            const data = await api('GET', '/admin/who');
            const players = data.players || data;
            const container = $('#online-table');

            if (Array.isArray(players) && players.length > 0) {
                let html = '<table><tr><th>Name</th><th>Entity ID</th><th>Level</th><th>Region</th><th>Actions</th></tr>';
                for (const p of players) {
                    const eid = p.entity_id || p.EntityId || '';
                    const name = p.name || p.Name || '—';
                    html += `<tr>
                        <td>${esc(name)}</td>
                        <td><code style="font-size:0.8em;">${esc(eid)}</code></td>
                        <td>${p.level || p.Level || '—'}</td>
                        <td>${esc(p.region || p.Region || '—')}</td>
                        <td>
                            <button class="btn btn-warning btn-sm" onclick="window._kick('${esc(eid)}','${esc(name)}')">เตะ</button>
                            <button class="btn btn-danger btn-sm" onclick="window._ban('${esc(eid)}','${esc(name)}')">แบน</button>
                        </td>
                    </tr>`;
                }
                html += '</table>';
                container.innerHTML = html;
            } else {
                container.innerHTML = '<div class="empty-state"><div class="icon">👤</div>ไม่มีผู้เล่นออนไลน์</div>';
            }
        } catch (err) {
            console.error('Load online error:', err);
        }
    }

    // Kick/Ban global functions
    window._kick = async (entityId, name) => {
        if (!confirm(`เตะ ${name} (${entityId})?`)) return;
        const result = await api('POST', '/admin/kick', { entity_id: entityId, reason: 'เตะโดยผู้ดูแล' });
        alert(result.kicked ? 'เตะสำเร็จ' : 'เตะไม่สำเร็จ');
        loadOnlinePlayers();
    };

    window._ban = async (entityId, name) => {
        if (!confirm(`แบน ${name} (${entityId})? ผู้เล่นจะถูกเตะออกทันที`)) return;
        const reason = prompt('เหตุผลที่แบน:', 'ถูกแบนโดยผู้ดูแล') || 'ถูกแบนโดยผู้ดูแล';
        const result = await api('POST', '/admin/ban', { entity_id: entityId, reason });
        alert(result.banned ? 'แบนสำเร็จ' : 'แบนไม่สำเร็จ: ' + (result.error || ''));
        loadOnlinePlayers();
    };

    // Refresh
    $('#btn-refresh-online').addEventListener('click', loadOnlinePlayers);

    // Whitelist
    async function loadWhitelist() {
        try {
            const data = await api('GET', '/admin/whitelist');
            if (data.entries) {
                $('#whitelist-editor').value = data.entries.join('\n');
            }
        } catch (err) {
            console.error('Load whitelist error:', err);
        }
    }

    $('#btn-save-whitelist').addEventListener('click', async () => {
        const status = $('#whitelist-status');
        const entries = $('#whitelist-editor').value;
        status.textContent = 'กำลังบันทึก...';
        status.className = 'status-text';
        try {
            const result = await api('POST', '/admin/whitelist', { entries });
            if (result.saved) {
                status.textContent = '✓ บันทึกสำเร็จ';
                status.className = 'status-text success';
            } else {
                status.textContent = '❌ ' + (result.error || '');
                status.className = 'status-text error';
            }
        } catch (err) {
            status.textContent = '❌ ' + err.message;
            status.className = 'status-text error';
        }
    });

    // Bans
    async function loadBans() {
        try {
            const data = await api('GET', '/admin/bans');
            const container = $('#bans-list');
            const bans = data.bans || data;

            if (Array.isArray(bans) && bans.length > 0) {
                let html = '<table><tr><th>Account Key</th><th>Reason</th><th>Date</th></tr>';
                for (const b of bans) {
                    html += `<tr><td><code>${esc(b.key || b.Key || '—')}</code></td><td>${esc(b.reason || b.Reason || '—')}</td><td>${esc(b.date || b.Date || '—')}</td></tr>`;
                }
                html += '</table>';
                container.innerHTML = html;
            } else {
                container.innerHTML = '<div class="empty-state"><div class="icon">✅</div>ไม่มีผู้เล่นที่ถูกแบน</div>';
            }
        } catch (err) {
            console.error('Load bans error:', err);
        }
    }

    // ══ Game Data Browser ═══════════════════════════════════════════════════════

    $('#btn-load-data').addEventListener('click', async () => {
        const select = $('#data-select');
        const asset = select.value;
        if (!asset) return;

        const viewer = $('#data-viewer');
        viewer.style.display = 'block';
        viewer.textContent = 'กำลังโหลด...';

        try {
            const data = await api('GET', `/assets/${asset}`);
            if (data._raw) {
                viewer.textContent = data._raw;
            } else {
                viewer.textContent = JSON.stringify(data, null, 2);
            }
        } catch (err) {
            viewer.textContent = 'Error: ' + err.message;
        }
    });

    // ══ Server Actions ══════════════════════════════════════════════════════════

    // Announce
    $('#btn-announce').addEventListener('click', async () => {
        const text = $('#announce-text').value.trim();
        if (!text) { alert('กรุณาใส่ข้อความ'); return; }
        const result = await api('POST', '/admin/announce', { text });
        const el = $('#announce-result');
        if (result.sent != null) {
            el.textContent = `✓ ส่งสำเร็จถึง ${result.sent} คน`;
            el.className = 'result-text success';
        } else {
            el.textContent = '❌ ' + (result.error || 'ไม่ทราบสาเหตุ');
            el.className = 'result-text error';
        }
    });

    // Maintenance
    $('#btn-maintenance-on').addEventListener('click', async () => {
        const result = await api('POST', '/admin/maintenance', { on: '1' });
        const el = $('#maintenance-result');
        el.textContent = result.maintenance ? '✓ เปิด Maintenance Mode แล้ว' : '❌ ' + (result.error || '');
        el.className = result.maintenance ? 'result-text success' : 'result-text error';
    });

    $('#btn-maintenance-off').addEventListener('click', async () => {
        const result = await api('POST', '/admin/maintenance', { on: '0' });
        const el = $('#maintenance-result');
        el.textContent = !result.maintenance ? '✓ ปิด Maintenance Mode แล้ว' : '❌ ' + (result.error || '');
        el.className = !result.maintenance ? 'result-text success' : 'result-text error';
    });

    // Reload
    $('#btn-reload-server').addEventListener('click', async () => {
        const el = $('#reload-result');
        el.textContent = 'กำลัง reload...';
        el.className = 'result-text';
        try {
            const result = await api('POST', '/admin/reload');
            if (result.reloaded) {
                el.textContent = '✓ Reload สำเร็จ';
                el.className = 'result-text success';
            } else {
                el.textContent = '❌ ' + (result.error || '');
                el.className = 'result-text error';
            }
        } catch (err) {
            el.textContent = '❌ ' + err.message;
            el.className = 'result-text error';
        }
    });

    // ══ Utilities ═══════════════════════════════════════════════════════════════

    function esc(s) {
        if (s == null) return '';
        return String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    }

    function formatUptime(sec) {
        if (sec == null) return '—';
        const h = Math.floor(sec / 3600);
        const m = Math.floor((sec % 3600) / 60);
        if (h > 0) return `${h}h ${m}m`;
        return `${m}m ${Math.floor(sec % 60)}s`;
    }

})();
