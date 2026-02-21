using System.Net.WebSockets;
using System.IO.MemoryMappedFiles;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://*:5000");
var app = builder.Build();
app.UseWebSockets();

string[] shmNames = { "amdipc_shm_daemon_140819_id0", "amdipc_shm_daemon_table_150805" };

app.Map("/ws", async context => {
    if (context.WebSockets.IsWebSocketRequest) {
        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        string currentShm = shmNames[0];
        _ = Task.Run(async () => {
            var buffer = new byte[1024];
            while (webSocket.State == WebSocketState.Open) {
                try {
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    var msg = Encoding.UTF8.GetString(buffer, 0, result.Count).Trim();
                    if (shmNames.Contains(msg)) currentShm = msg;
                } catch { break; }
            }
        });

        while (webSocket.State == WebSocketState.Open) {
            try {
                using var mhf = MemoryMappedFile.OpenExisting(currentShm);
                using var accessor = mhf.CreateViewAccessor();
                byte[] data = new byte[accessor.Capacity];
                accessor.ReadArray(0, data, 0, data.Length);
                await webSocket.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Binary, true, CancellationToken.None);
            } catch { }
            await Task.Delay(200);
        }
    }
});

app.MapGet("/", () => Results.Content(GetHtml(), "text/html"));
app.Run();

string GetHtml() => @"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <title>SHM Ultimate Analyzer</title>
    <style>
        :root { --bg: #1e1e1e; --sidebar: #252526; --accent: #007acc; --text: #cccccc; --hi: #f1c40f; --zero: #555; }
        * { box-sizing: border-box; }
        body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; background: var(--bg); color: var(--text); margin: 0; display: flex; height: 100vh; overflow: hidden; }
        
        /* 1. 全局自定义滚动条 */
        ::-webkit-scrollbar { width: 12px; height: 12px; }
        ::-webkit-scrollbar-track { background: transparent; }
        ::-webkit-scrollbar-thumb { background: #424242; border: 3px solid var(--bg); border-radius: 8px; }
        ::-webkit-scrollbar-thumb:hover { background: #555; }

        /* 侧边栏 */
        .sidebar { width: 160px; background: var(--sidebar); border-right: 1px solid #111; display: flex; flex-direction: column; flex-shrink: 0; z-index: 30; box-shadow: 2px 0 8px rgba(0,0,0,0.2); }
        .brand { padding: 18px 15px; font-weight: 800; color: var(--accent); font-size: 12px; letter-spacing: 1px; border-bottom: 1px solid #111; background: #1a1a1c; }
        .nav-item { width: 100%; padding: 14px 15px; background: transparent; color: #888; border: none; text-align: left; cursor: pointer; font-size: 11px; transition: all 0.2s; border-left: 3px solid transparent; font-weight: 500; }
        .nav-item:hover:not(.active) { color: #bbb; background: rgba(255,255,255,0.02); }
        .nav-item.active { background: #2a2d2e; color: #fff; border-left-color: var(--accent); box-shadow: inset 2px 0 5px rgba(0,0,0,0.1); }
        
        .main-wrapper { flex: 1; display: flex; flex-direction: column; position: relative; min-width: 0; background: #1e1e1e; }
        
        /* 顶部状态栏及阴影 */
        .top-bar { height: 40px; background: #252526; border-bottom: 1px solid #111; display: flex; align-items: center; padding: 0 20px; font-size: 11px; color: #aaa; gap: 20px; z-index: 20; box-shadow: 0 2px 8px rgba(0,0,0,0.15); }
        
        /* 3. 呼吸灯效果 */
        @keyframes pulse-dot {
            0% { box-shadow: 0 0 0 0 rgba(78,201,176, 0.4); }
            70% { box-shadow: 0 0 0 6px rgba(78,201,176, 0); }
            100% { box-shadow: 0 0 0 0 rgba(78,201,176, 0); }
        }
        .status-dot { width: 8px; height: 8px; border-radius: 50%; background: #444; }
        .online { background: #4ec9b0 !important; animation: pulse-dot 2s infinite; }

        .hex-header { height: 30px; background: rgba(30,30,30,0.95); border-bottom: 1px solid #2d2d2d; display: flex; align-items: center; padding-left: 105px; font-family: 'Consolas', monospace; font-size: 13px; color: #777; user-select: none; z-index: 10; position: relative; }
        .hex-unit { width: 22px; text-align: center; margin-right: 4px; }
        .mid-gap { margin-right: 12px; }

        .viewport { flex: 1; display: flex; position: relative; overflow: hidden; }
        #scrollContainer { flex: 1; overflow-y: auto; position: relative; z-index: 5; scroll-behavior: smooth; }
        #virtualSpacer { position: absolute; top: 0; left: 0; width: 100%; visibility: hidden; }
        #contentLayer { position: absolute; top: 0; left: 0; right: 0; padding: 5px 15px; font-family: 'Consolas', monospace; font-size: 13.5px; line-height: 22px; z-index: 6; user-select: text; }

        .minimap-area { width: 80px; position: absolute; top: 0; right: 12px; height: 100%; background: rgba(0,0,0,0.2); border-left: 1px solid #222; z-index: 10; cursor: pointer; backdrop-filter: blur(2px); }
        #mmCanvas { width: 100%; position: absolute; top: 0; pointer-events: none; image-rendering: pixelated; opacity: 0.85; }
        #mmSlider { position: absolute; width: 100%; background: rgba(0, 122, 204, 0.15); border: 1px solid rgba(0, 122, 204, 0.4); pointer-events: none; border-radius: 2px; transition: top 0.05s linear; }

        /* 2. 数据行悬停反馈 */
        .row { display: flex; height: 22px; align-items: center; white-space: pre; border-radius: 3px; border: 1px solid transparent; transition: background 0.1s; }
        .row:hover:not(.folded-row):not(.selected) { background: rgba(255, 255, 255, 0.03); border-color: rgba(255, 255, 255, 0.05); }
        .row.selected { background: rgba(0, 122, 204, 0.2) !important; border-color: rgba(0, 122, 204, 0.5); box-shadow: inset 0 0 8px rgba(0,0,0,0.2); }
        
        /* 4. 精美的胶囊状折叠标签 */
        .row.folded-row { position: relative; justify-content: center; margin: 4px 0; height: 26px; border: none; background: transparent; }
        .row.folded-row::before { content: ''; position: absolute; left: 0; right: 0; top: 50%; height: 1px; background: rgba(255,255,255,0.05); z-index: 1; }
        .folded-badge { position: relative; z-index: 2; background: #1a1a1a; padding: 0 16px; color: #666; font-size: 11px; font-family: 'Segoe UI', sans-serif; border: 1px solid #333; border-radius: 12px; display: inline-flex; align-items: center; gap: 6px; box-shadow: 0 2px 4px rgba(0,0,0,0.3); }
        
        .offset { color: #569cd6; width: 80px; margin-right: 10px; user-select: none; font-weight: 500; opacity: 0.9; }
        .hex-part { display: flex; }
        .byte { width: 22px; text-align: center; margin-right: 4px; display: inline-block; }
        .ascii-part { color: #ce9178; margin-left: 15px; border-left: 1px solid #333; padding-left: 15px; opacity: 0.85; }
        
        .v-00 { color: var(--zero); font-weight: 300; }
        .hi { background: var(--hi); color: #000 !important; font-weight: bold; border-radius: 3px; box-shadow: 0 0 5px rgba(241, 196, 15, 0.4); text-shadow: none; padding: 0 1px; }

        /* 5. 侧边栏面板阴影 */
        .inspector { width: 310px; background: var(--sidebar); border-left: 1px solid #111; padding: 20px; flex-shrink: 0; z-index: 20; display: flex; flex-direction: column; box-shadow: -2px 0 10px rgba(0,0,0,0.2); }
        .ins-title { font-size: 10px; color: #777; text-transform: uppercase; margin-bottom: 12px; border-bottom: 1px solid #333; padding-bottom: 8px; font-weight: bold; letter-spacing: 0.5px; }
        .ins-row { display: flex; justify-content: space-between; margin-bottom: 10px; font-family: 'Consolas', monospace; font-size: 13px; align-items: center; }
        .ins-row span:first-child { color: #888; }
        .ins-val { color: #4ec9b0; font-weight: bold; background: rgba(78,201,176,0.1); padding: 2px 6px; border-radius: 4px; }
        
        .select-mini { background: #1e1e1e; color: #ccc; border: 1px solid #444; border-radius: 4px; font-size: 11px; padding: 4px 8px; outline: none; cursor: pointer; transition: border 0.2s; }
        .select-mini:hover { border-color: var(--accent); }
        
        /* UI 控件美化 */
        input[type='checkbox'] { accent-color: var(--accent); cursor: pointer; width: 14px; height: 14px; }
    </style>
</head>
<body>
    <div class='sidebar'>
        <div class='brand'>SHM ANALYZER</div>
        <div class='nav'>
            <button class='nav-item active' onclick='switchShm(""amdipc_shm_daemon_140819_id0"", this)'>DAEMON_140819</button>
            <button class='nav-item' onclick='switchShm(""amdipc_shm_daemon_table_150805"", this)'>TABLE_150805</button>
        </div>
    </div>
    
    <div class='main-wrapper'>
        <div class='top-bar'>
            <div style='display:flex; align-items:center; gap:10px;'>
                <div id='status-dot' class='status-dot online'></div>
                <span id='status-text' style='width:40px; font-weight:600; color:#eee;'>LIVE</span>
            </div>
            <div style='border-left: 1px solid #333; padding-left: 15px; display: flex; align-items: center;'>
                <label style='cursor:pointer; display:flex; align-items:center; gap:6px; user-select:none;'>
                    <input type='checkbox' id='foldToggle' checked onchange='forceRebuild()'> 
                    <span>Auto Fold Zeros</span>
                </label>
            </div>
            <span id='pos-info' style='color:var(--accent); font-family:monospace; margin-left:auto; font-size:12px; background:rgba(0,122,204,0.1); padding:4px 8px; border-radius:4px;'>OFFSET: 0x00000000</span>
        </div>

        <div class='hex-header' id='header'>
            <div class='hex-unit'>00</div><div class='hex-unit'>01</div><div class='hex-unit'>02</div><div class='hex-unit'>03</div>
            <div class='hex-unit'>04</div><div class='hex-unit'>05</div><div class='hex-unit'>06</div><div class='hex-unit mid-gap'>07</div>
            <div class='hex-unit'>08</div><div class='hex-unit'>09</div><div class='hex-unit'>0A</div><div class='hex-unit'>0B</div>
            <div class='hex-unit'>0C</div><div class='hex-unit'>0D</div><div class='hex-unit'>0E</div><div class='hex-unit'>0F</div>
        </div>

        <div class='viewport'>
            <div id='scrollContainer'>
                <div id='virtualSpacer'></div>
                <div id='contentLayer'></div>
            </div>
            <div class='minimap-area' id='mmArea'>
                <canvas id='mmCanvas'></canvas>
                <div id='mmSlider'></div>
            </div>
        </div>
    </div>

    <div class='inspector'>
        <div>
            <div class='ins-title'>Data Inspector</div>
            <div id='ins-content'><div style='color:#666; font-size:12px; font-style:italic; text-align:center; padding: 20px 0;'>Select a row to analyze</div></div>
        </div>
        
        <div style='margin-top: 30px; flex: 1; display: flex; flex-direction: column; overflow: hidden;'>
            <div class='ins-title' style='display:flex; justify-content:space-between; align-items:center;'>
                <span>Change Map</span>
                <select class='select-mini' id='chunkSelect' onchange='forceRebuild()'>
                    <option value='256'>256 B</option>
                    <option value='1024' selected>1 KB</option>
                    <option value='4096'>4 KB</option>
                </select>
            </div>
            <div id='changeMapContainer' style='flex:1; overflow-y:auto; background:#18181a; border:1px solid #2a2a2a; border-radius: 6px; padding: 8px; box-shadow: inset 0 2px 5px rgba(0,0,0,0.2);'>
                <canvas id='changeCanvas' style='display:block; cursor:crosshair; width: 100%;'></canvas>
            </div>
        </div>
    </div>

    <script>
        let ws, fullData = null, lastData = null;
        let selectedOffset = -1; 
        let visualMap = []; 
        let mouseDownTime = 0, startY = 0;
        
        const ROW_H = 22, BYTES_P_R = 16, MM_ROW_H = 2; // 高度增加，呼吸感更好
        const scrollContainer = document.getElementById('scrollContainer');
        const contentLayer = document.getElementById('contentLayer');
        const mmArea = document.getElementById('mmArea');
        const mmCanvas = document.getElementById('mmCanvas');
        const changeCanvas = document.getElementById('changeCanvas');
        const mmSlider = document.getElementById('mmSlider');
        const mmCtx = mmCanvas.getContext('2d', {alpha: false});

        function connect() {
            ws = new WebSocket('ws://' + location.host + '/ws');
            ws.onmessage = async (e) => {
                if(typeof e.data === 'string') return;
                fullData = new Uint8Array(await e.data.arrayBuffer());
                
                buildVisualMap();
                
                if (window.getSelection().isCollapsed) {
                    render();
                    drawMinimap();
                    drawChangeMap();
                    if(selectedOffset !== -1) updateInspector();
                }
                lastData = new Uint8Array(fullData);
            };
        }

        function buildVisualMap() {
            visualMap = [];
            const doFold = document.getElementById('foldToggle').checked;
            const rowCount = Math.ceil(fullData.length / BYTES_P_R);
            let zeroRows = 0;
            let zeroStart = 0;
            
            for (let i = 0; i < rowCount; i++) {
                let isZero = true;
                for(let j=0; j<BYTES_P_R; j++) {
                    const idx = i*BYTES_P_R + j;
                    if (idx < fullData.length && fullData[idx] !== 0) { isZero = false; break; }
                }

                if (isZero && doFold) {
                    if (zeroRows === 0) zeroStart = i;
                    zeroRows++;
                } else {
                    if (zeroRows > 0) {
                        if (zeroRows > 2) visualMap.push({ type: 'fold', offset: zeroStart*16, skipRows: zeroRows });
                        else for(let z=0; z<zeroRows; z++) visualMap.push({ type: 'hex', offset: (zeroStart+z)*16 });
                        zeroRows = 0;
                    }
                    visualMap.push({ type: 'hex', offset: i*16 });
                }
            }
            if (zeroRows > 2) visualMap.push({ type: 'fold', offset: zeroStart*16, skipRows: zeroRows });
            else if (zeroRows > 0) for(let z=0; z<zeroRows; z++) visualMap.push({ type: 'hex', offset: (zeroStart+z)*16 });
        }

        function forceRebuild() {
            if(!fullData) return;
            buildVisualMap(); render(); drawMinimap(); drawChangeMap();
        }

        changeCanvas.onmousedown = (e) => {
            const rect = changeCanvas.getBoundingClientRect();
            const chunkSize = parseInt(document.getElementById('chunkSelect').value);
            // 这里根据实际绘制的块大小（bw:10 + gap:2 = 12）计算
            const x = Math.floor((e.clientX - rect.left) / 12);
            const y = Math.floor((e.clientY - rect.top) / 12);
            const cols = Math.floor(changeCanvas.width / 12);
            const chunkIdx = y * cols + x;
            jumpToOffset(chunkIdx * chunkSize);
        };

        function jumpToOffset(targetOffset) {
            for(let i=0; i<visualMap.length; i++) {
                const v = visualMap[i];
                if (v.type === 'hex' && v.offset >= targetOffset) { scrollContainer.scrollTop = i * ROW_H; return; }
                else if (v.type === 'fold' && targetOffset >= v.offset && targetOffset < v.offset + v.skipRows * 16) { scrollContainer.scrollTop = i * ROW_H; return; }
            }
        }

        scrollContainer.onmousedown = (e) => { mouseDownTime = Date.now(); startY = e.clientY; };
        scrollContainer.onmouseup = (e) => {
            const dist = Math.abs(e.clientY - startY);
            if ((Date.now() - mouseDownTime) < 200 && dist < 5) {
                const rect = scrollContainer.getBoundingClientRect();
                const clickedRow = Math.floor((e.clientY - rect.top + scrollContainer.scrollTop - 5) / ROW_H);
                if (clickedRow >= 0 && clickedRow < visualMap.length) {
                    const vRow = visualMap[clickedRow];
                    if (vRow.type === 'hex') {
                        selectedOffset = vRow.offset;
                        render(); updateInspector();
                        document.getElementById('pos-info').innerText = `OFFSET: 0x${selectedOffset.toString(16).toUpperCase().padStart(8,'0')}`;
                    }
                }
            }
        };

        function render() {
            if(!fullData) return;
            document.getElementById('virtualSpacer').style.height = (visualMap.length * ROW_H) + 'px';

            const startRow = Math.floor(scrollContainer.scrollTop / ROW_H);
            const endRow = Math.min(startRow + Math.ceil(scrollContainer.clientHeight / ROW_H) + 2, visualMap.length);
            
            contentLayer.style.transform = `translateY(${startRow * ROW_H}px)`;
            
            let html = '';
            for (let i = startRow; i < endRow; i++) {
                const vRow = visualMap[i];
                if (vRow.type === 'fold') {
                    // 更新为美化后的胶囊徽章样式
                    html += `<div class='row folded-row'><div class='folded-badge'>⯆ ${vRow.skipRows * 16} Bytes Folded at 0x${vRow.offset.toString(16).padStart(8,'0').toUpperCase()}</div></div>`;
                    continue;
                }

                const offset = vRow.offset;
                const isSelected = (selectedOffset === offset) ? ' selected' : '';
                let hStr = '', aStr = '';
                for (let j = 0; j < BYTES_P_R; j++) {
                    const idx = offset + j;
                    if (idx < fullData.length) {
                        const val = fullData[idx];
                        const changed = lastData && lastData[idx] !== val;
                        let cls = (val === 0) ? 'v-00' : '';
                        if (changed) cls += ' hi'; // 支持多类名组合
                        const clsAttr = cls.trim() ? ` class=""${cls.trim()}""` : '';
                        const gapCls = (j === 7) ? ' mid-gap' : '';
                        hStr += `<div class='byte${gapCls}'><span${clsAttr}>${val.toString(16).padStart(2,'0').toUpperCase()}</span></div>`;
                        const c = (val >= 32 && val <= 126) ? String.fromCharCode(val) : '.';
                        aStr += `<span${clsAttr}>${c === ' ' ? '&nbsp;' : c}</span>`;
                    }
                }
                html += `<div class='row${isSelected}'><span class='offset'>${offset.toString(16).padStart(8,'0').toUpperCase()}</span><div class='hex-part'>${hStr}</div><div class='ascii-part'>${aStr}</div></div>`;
            }
            contentLayer.innerHTML = html;
            syncMinimap();
        }

        // 6. 优化后的矩阵雷达图绘制
        function drawChangeMap() {
            if (!fullData) return;
            const container = document.getElementById('changeMapContainer');
            const chunkSize = parseInt(document.getElementById('chunkSelect').value);
            const chunks = Math.ceil(fullData.length / chunkSize);
            
            // 加入 Gap（间隙），使得每个块变成真正的网格
            const blockSize = 10;
            const gap = 2;
            const totalSize = blockSize + gap;
            
            const cols = Math.floor(container.clientWidth / totalSize);
            const rows = Math.ceil(chunks / cols);
            
            changeCanvas.width = cols * totalSize;
            changeCanvas.height = rows * totalSize;
            const ctx = changeCanvas.getContext('2d');
            ctx.clearRect(0, 0, changeCanvas.width, changeCanvas.height);
            
            for(let i=0; i<chunks; i++) {
                const start = i * chunkSize;
                const end = Math.min(start + chunkSize, fullData.length);
                let changed = false, allZero = true;
                
                for(let j=start; j<end; j++) {
                    if (lastData && lastData[j] !== fullData[j]) changed = true;
                    if (fullData[j] !== 0) allZero = false;
                    if (changed && !allZero) break; 
                }
                
                const x = (i % cols) * totalSize;
                const y = Math.floor(i / cols) * totalSize;
                
                if (changed) ctx.fillStyle = '#f1c40f'; // 闪烁金黄
                else if (allZero) ctx.fillStyle = '#222224'; // 暗淡空底
                else ctx.fillStyle = '#007acc'; // 活跃数据蓝
                
                // 绘制带微小圆角的矩形(如果浏览器支持，否则 fallback 为普通矩形)
                if(ctx.roundRect) {
                    ctx.beginPath();
                    ctx.roundRect(x, y, blockSize, blockSize, 2);
                    ctx.fill();
                } else {
                    ctx.fillRect(x, y, blockSize, blockSize);
                }
            }
        }

        function updateInspector() {
            if(selectedOffset === -1 || !fullData) return;
            const dv = new DataView(fullData.buffer, selectedOffset, Math.min(16, fullData.length - selectedOffset));
            const getSafe = (f) => { try { return f(); } catch(e) { return 'N/A'; } };
            document.getElementById('ins-content').innerHTML = `
                <div class='ins-row'><span>Offset</span><span class='ins-val'>0x${selectedOffset.toString(16).toUpperCase()}</span></div>
                <div class='ins-row'><span>Int32</span><span class='ins-val'>${getSafe(()=>dv.getInt32(0,true))}</span></div>
                <div class='ins-row'><span>Float32</span><span class='ins-val'>${getSafe(()=>dv.getFloat32(0,true).toFixed(4))}</span></div>
                <div style='color:#ce9178; background:#1e1e1e; padding:10px; font-size:13px; border-radius:6px; margin-top:15px; font-family:monospace; word-break:break-all; border: 1px solid #333; box-shadow: inset 0 2px 4px rgba(0,0,0,0.1);'>
                    ${new TextDecoder().decode(fullData.slice(selectedOffset, selectedOffset+16)).replace(/\0/g,' ')}
                </div>
            `;
        }

        function syncMinimap() {
            const ratio = MM_ROW_H / ROW_H;
            const viewH = scrollContainer.clientHeight;
            const mmTotalH = visualMap.length * MM_ROW_H;
            mmSlider.style.height = (viewH * ratio) + 'px';
            let mmTranslate = 0;
            if(mmTotalH > viewH) {
                const scrollPercent = scrollContainer.scrollTop / (visualMap.length * ROW_H - viewH);
                mmTranslate = scrollPercent * (mmTotalH - viewH);
            }
            mmCanvas.style.transform = `translateY(${-mmTranslate}px)`;
            mmSlider.style.top = (scrollContainer.scrollTop * ratio - mmTranslate) + 'px';
        }

        function drawMinimap() {
            if(!fullData) return;
            mmCanvas.width = 80; mmCanvas.height = visualMap.length * MM_ROW_H;
            const imgData = mmCtx.createImageData(mmCanvas.width, mmCanvas.height);
            for(let i=0; i < visualMap.length; i++) {
                const vRow = visualMap[i];
                if (vRow.type === 'fold') {
                    for(let col=0; col<16; col++) {
                        for(let m=0; m<MM_ROW_H; m++) {
                            const pIdx = ((i * MM_ROW_H + m) * 80 + (col * 4)) * 4;
                            imgData.data[pIdx]=20; imgData.data[pIdx+1]=20; imgData.data[pIdx+2]=20; imgData.data[pIdx+3]=255;
                        }
                    }
                } else {
                    for(let col=0; col<16; col++) {
                        const idx = vRow.offset + col;
                        if (idx >= fullData.length) break;
                        const changed = lastData && lastData[idx] !== fullData[idx];
                        for(let m=0; m<MM_ROW_H; m++) {
                            const pIdx = ((i * MM_ROW_H + m) * 80 + (col * 4)) * 4;
                            const b = (fullData[idx] === 0) ? 35 : (fullData[idx]/3 + 60);
                            imgData.data[pIdx]=changed?241:b; imgData.data[pIdx+1]=changed?196:b; imgData.data[pIdx+2]=changed?15:b; imgData.data[pIdx+3]=255;
                        }
                    }
                }
            }
            mmCtx.putImageData(imgData, 0, 0);
        }

        mmArea.onmousedown = (e) => {
            const move = (ev) => {
                const rect = mmArea.getBoundingClientRect();
                const scrollPercent = (ev.clientY - rect.top) / rect.height;
                scrollContainer.scrollTop = scrollPercent * (visualMap.length * ROW_H) - (scrollContainer.clientHeight / 2);
            };
            move(e); window.onmousemove = move; window.onmouseup = () => { window.onmousemove = null; };
        };

        // 响应式重绘雷达图
        window.onresize = drawChangeMap;
        scrollContainer.onscroll = render;
        connect();
        function switchShm(name, btn) {
            document.querySelectorAll('.nav-item').forEach(b => b.classList.remove('active'));
            btn.classList.add('active');
            ws.send(name); lastData = null; selectedOffset = -1; scrollContainer.scrollTop = 0; forceRebuild();
        }
    </script>
</body>
</html>";