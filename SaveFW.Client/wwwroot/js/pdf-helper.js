window.PdfHelper = {
    captureMapAndGenerate: async function (mapElementId)
    {
        const mapContainer = document.getElementById(mapElementId);
        let base64 = null;

        if (!mapContainer) return null;

        try
        {
            // Access the MapLibre GL map instance via the global reference
            const mapInstance = window.MapLibreImpactMap && window.MapLibreImpactMap.getMap
                ? window.MapLibreImpactMap.getMap()
                : null;

            console.log('[PDF] Map instance:', mapInstance ? 'found' : 'NOT FOUND');

            if (mapInstance)
            {
                const glCanvas = mapInstance.getCanvas();
                const width = glCanvas.width;
                const height = glCanvas.height;
                const dpr = window.devicePixelRatio || 1;

                console.log('[PDF] GL canvas size:', width, 'x', height, 'DPR:', dpr);

                // Try Method 1: Direct WebGL canvas export (works when tiles are same-origin)
                let glExportSuccess = false;
                let glDataUrl = null;
                try {
                    glDataUrl = glCanvas.toDataURL('image/png');
                    glExportSuccess = glDataUrl && glDataUrl.length > 100;
                    console.log('[PDF] WebGL toDataURL:', glExportSuccess ? 'SUCCESS' : 'FAILED (empty)', 'length:', glDataUrl?.length);
                } catch (e) {
                    console.warn('[PDF] WebGL toDataURL threw (canvas tainted by cross-origin tiles):', e.message);
                }

                if (glExportSuccess)
                {
                    // Clean path: composite WebGL canvas + DOM overlays
                    const compositeCanvas = document.createElement('canvas');
                    compositeCanvas.width = width;
                    compositeCanvas.height = height;
                    const ctx = compositeCanvas.getContext('2d');

                    // Draw the map
                    const mapImg = new Image();
                    mapImg.src = glDataUrl;
                    await new Promise(resolve => { mapImg.onload = resolve; });
                    ctx.drawImage(mapImg, 0, 0);

                    // Overlay DOM elements (markers, popups)
                    try {
                        const overlayCanvas = await html2canvas(mapContainer, {
                            useCORS: true,
                            allowTaint: true,
                            logging: false,
                            scale: dpr,
                            backgroundColor: null,
                            ignoreElements: (el) => {
                                if (el.tagName === 'CANVAS' && el.classList.contains('maplibregl-canvas')) return true;
                                if (el.id === 'map-navigation-overlay') return true;
                                if (el.id === 'map-zoom-hint') return true;
                                if (el.id === 'map-overlay-panel') return true;
                                if (el.id === 'map-overlay-topright') return true;
                                if (el.classList && el.classList.contains('maplibregl-ctrl-top-right')) return true;
                                if (el.classList && el.classList.contains('maplibregl-ctrl-bottom-right')) return true;
                                if (el.classList && el.classList.contains('maplibregl-ctrl-top-left')) return true;
                                if (el.classList && el.classList.contains('maplibregl-ctrl-bottom-left')) return true;
                                return false;
                            }
                        });
                        ctx.drawImage(overlayCanvas, 0, 0, width, height);
                        console.log('[PDF] Overlays composited');
                    } catch (overlayErr) {
                        console.warn('[PDF] Overlay capture failed:', overlayErr);
                    }

                    base64 = compositeCanvas.toDataURL('image/png');
                    console.log('[PDF] Export successful via WebGL path, length:', base64.length);
                }
                else
                {
                    // Fallback: Canvas is tainted by cross-origin raster tiles.
                    // Use html2canvas with allowTaint for full container capture.
                    // html2canvas can't render WebGL, but it captures DOM + the tainted
                    // canvas pixels when allowTaint is true.
                    console.log('[PDF] Using html2canvas full capture fallback (tainted canvas)');
                    const canvas = await html2canvas(mapContainer, {
                        useCORS: true,
                        allowTaint: true,
                        logging: false,
                        scale: dpr,
                        backgroundColor: '#0f172a',
                        ignoreElements: (el) => {
                            if (el.id === 'map-navigation-overlay') return true;
                            if (el.id === 'map-zoom-hint') return true;
                            if (el.id === 'map-overlay-panel') return true;
                            if (el.id === 'map-overlay-topright') return true;
                            if (el.classList && el.classList.contains('maplibregl-ctrl-top-right')) return true;
                            if (el.classList && el.classList.contains('maplibregl-ctrl-bottom-right')) return true;
                            if (el.classList && el.classList.contains('maplibregl-ctrl-top-left')) return true;
                            if (el.classList && el.classList.contains('maplibregl-ctrl-bottom-left')) return true;
                            return false;
                        }
                    });
                    base64 = canvas.toDataURL('image/png');
                    console.log('[PDF] Fallback export, length:', base64.length);
                }
            } else
            {
                // No MapLibre instance at all
                console.warn('[PDF] MapLibre instance not found, falling back to html2canvas');
                const canvas = await html2canvas(mapContainer, {
                    useCORS: true,
                    allowTaint: true,
                    logging: false,
                    scale: 2
                });
                base64 = canvas.toDataURL('image/png');
            }
        } catch (err)
        {
            console.error('[PDF] Map capture failed:', err);
        }

        return base64;
    },

    downloadFileFromStream: async function (filename, contentStreamReference)
    {
        const arrayBuffer = await contentStreamReference.arrayBuffer();
        const blob = new Blob([arrayBuffer], { type: 'application/pdf' });
        const url = URL.createObjectURL(blob);
        const anchorElement = document.createElement('a');
        anchorElement.href = url;
        anchorElement.download = filename ?? '';
        anchorElement.click();
        anchorElement.remove();
        URL.revokeObjectURL(url);
    },

    getReportData: function ()
    {
        // 1. Scrape Main Table (Net Economic Impact)
        const table = document.querySelector('#net-impact-table table');
        let mainTableData = { headers: [], rows: [] };

        if (table)
        {
            const ths = Array.from(table.querySelectorAll('thead th'));
            mainTableData.headers = ths.map(th => th.innerText.trim());
            const trs = Array.from(table.querySelectorAll('tbody tr'));
            mainTableData.rows = trs.map(tr =>
            {
                const cells = Array.from(tr.querySelectorAll('td'));
                return cells.map(td => td.innerText.trim().replace(/[\n\r]+|info/g, ' '));
            });
        }

        // 2. Scrape Supplementary Tables (Breakdowns)
        const getRow = (label, idVictims, idPer, idTotal) =>
        {
            return [
                label,
                document.getElementById(idVictims)?.innerText || "-",
                document.getElementById(idPer)?.innerText || "-",
                document.getElementById(idTotal)?.innerText || "-"
            ];
        };

        const breakdownSubjectData = [
            getRow("Public Health", "calc-break-health-victims", "calc-break-health-per", "calc-break-health-total"),
            getRow("Social Services", "calc-break-social-victims", "calc-break-social-per", "calc-break-social-total"),
            getRow("Law Enforcement", "calc-break-crime-victims", "calc-break-crime-per", "calc-break-crime-total"),
            getRow("Civil Legal", "calc-break-legal-victims", "calc-break-legal-per", "calc-break-legal-total"),
            getRow("Abused Dollars", "calc-break-abused-victims", "calc-break-abused-per", "calc-break-abused-total"),
            getRow("Lost Employment", "calc-break-employment-victims", "calc-break-employment-per", "calc-break-employment-total"),
            getRow("Total", "calc-break-total-victims", "calc-total-cost-per", "calc-total-cost-combined")
        ];

        const breakdownOtherData_Scraped = [
            getRow("Public Health", "calc-break-health-victims-other", "calc-break-health-per-other", "calc-break-health-total-other"),
            getRow("Social Services", "calc-break-social-victims-other", "calc-break-social-per-other", "calc-break-social-total-other"),
            getRow("Law Enforcement", "calc-break-crime-victims-other", "calc-break-crime-per-other", "calc-break-crime-total-other"),
            getRow("Civil Legal", "calc-break-legal-victims-other", "calc-break-legal-per-other", "calc-break-legal-total-other"),
            getRow("Abused Dollars", "calc-break-abused-victims-other", "calc-break-abused-per-other", "calc-break-abused-total-other"),
            getRow("Lost Employment", "calc-break-employment-victims-other", "calc-break-employment-per-other", "calc-break-employment-total-other"),
            getRow("Total", "calc-break-total-victims-other", "calc-total-cost-per-other", "calc-total-cost-combined-other")
        ];

        // 3. Scrape Analysis Text (Markdown-ish)
        const analysisEl = document.getElementById('analysis-text');
        let formattedText = "";

        // Recursive list processor
        const processList = (ul, level = 0) =>
        {
            let result = "";
            const indent = " ".repeat(level * 2);
            for (const child of ul.children)
            {
                if (child.tagName === 'LI')
                {
                    // Clone to get text without child lists
                    const clone = child.cloneNode(true);
                    const nested = clone.querySelectorAll('ul, ol');
                    nested.forEach(n => n.remove());

                    let liText = clone.innerHTML;
                    liText = liText.replace(/<(strong|b)>(.*?)<\/\1>/gi, "**$2**");
                    liText = liText.replace(/<[^>]+>/g, ""); // Strip other tags
                    liText = liText.trim();

                    if (liText)
                    {
                        result += `${indent}* ${liText}\n`;
                    }

                    // Process nested lists from original child
                    for (const sub of child.children)
                    {
                        if (sub.tagName === 'UL' || sub.tagName === 'OL')
                        {
                            result += processList(sub, level + 1);
                        }
                    }
                }
            }
            return result;
        };

        if (analysisEl)
        {
            for (const node of analysisEl.childNodes)
            {
                if (node.nodeType === Node.ELEMENT_NODE)
                {
                    if (node.tagName === 'DIV' && node.classList.contains('font-bold'))
                    {
                        formattedText += `### ${node.innerText.trim()}\n`;
                    } else if (node.tagName === 'UL')
                    {
                        formattedText += processList(node);
                        formattedText += "\n";
                    } else if (node.tagName === 'P')
                    {
                        formattedText += `${node.innerText.trim()}\n\n`;
                    }
                }
            }
        }

        // 4. Retrieve Detailed Calc Data
        let calcData = null;
        if (window.EconomicCalculator && window.EconomicCalculator.getLastCalculationData)
        {
            calcData = window.EconomicCalculator.getLastCalculationData();
        }

        const subjectCountyName = (calcData && calcData.subjectCountyName) ? calcData.subjectCountyName : null;

        // 5. Build Final Other Breakdown
        let breakdownOtherData = [];
        const fmtM = (v) => '$' + (v / 1000000).toFixed(1) + 'MM';

        if (calcData && calcData.otherCosts && Array.isArray(calcData.otherCosts.counties) && calcData.otherCosts.counties.length > 0)
        {
            const counties = calcData.otherCosts.counties;
            breakdownOtherData = counties.map(c =>
            {
                return [
                    c.name + " County",
                    fmtM(c.costs.health),
                    fmtM(c.costs.social),
                    fmtM(c.costs.crime),
                    fmtM(c.costs.legal),
                    fmtM(c.costs.abused),
                    fmtM(c.costs.employment),
                    fmtM(c.costs.total)
                ];
            });
        } else
        {
            // Use scraped summary if detailed data missing (fallback)
            // But scraped data is [Category, Victims, Per, Total] (rows).
            // We need to transpose it to fit the new table structure [Name, PH, SS, ...]
            // The scraped data is just ONE row of values effectively (the "Regional Spillover" aggregate).
            // Let's manually construct a single summary row from the scraped data to fit the new schema.
            // Scraped indices: 0=PH, 1=SS, 2=Law, 3=Legal, 4=Abused, 5=Emp, 6=Total.
            // Value is at index 3 of each row.
            const val = (idx) => breakdownOtherData_Scraped[idx][3];

            breakdownOtherData = [[
                "Regional Spillover (Summary)",
                val(0), val(1), val(2), val(3), val(4), val(5), val(6)
            ]];
        }

        return {
            subjectCountyName: subjectCountyName,
            analysisText: formattedText,
            mainTable: mainTableData,
            breakdownTable: breakdownSubjectData,
            breakdownOtherTable: breakdownOtherData
        };
    }
};