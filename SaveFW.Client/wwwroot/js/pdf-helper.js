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

            if (mapInstance)
            {
                // Strategy: Composite MapLibre's WebGL canvas + DOM overlays (markers)
                const glCanvas = mapInstance.getCanvas();
                const width = glCanvas.width;
                const height = glCanvas.height;
                const dpr = window.devicePixelRatio || 1;

                // Create compositing canvas
                const compositeCanvas = document.createElement('canvas');
                compositeCanvas.width = width;
                compositeCanvas.height = height;
                const ctx = compositeCanvas.getContext('2d');

                // 1. Draw the WebGL map canvas (tiles, vectors, isochrones, etc.)
                ctx.drawImage(glCanvas, 0, 0);

                // 2. Capture DOM overlays (markers, popups) via html2canvas
                // MapLibre renders markers as absolutely positioned DOM elements
                // inside .maplibregl-canvas-container's sibling containers
                try
                {
                    const overlayCanvas = await html2canvas(mapContainer, {
                        useCORS: true,
                        allowTaint: true,
                        logging: false,
                        scale: dpr,
                        backgroundColor: null, // Transparent so we only get DOM overlays
                        ignoreElements: (el) =>
                        {
                            // Ignore the WebGL canvas itself (we already drew it)
                            if (el.tagName === 'CANVAS' && el.classList.contains('maplibregl-canvas')) return true;
                            // Ignore controls we don't want in PDF
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
                    // Draw marker/popup overlays on top of the map
                    ctx.drawImage(overlayCanvas, 0, 0, width, height);
                } catch (overlayErr)
                {
                    console.warn("Overlay capture failed, map still captured:", overlayErr);
                }

                base64 = compositeCanvas.toDataURL("image/png");
            } else
            {
                // Fallback: try html2canvas for the whole container (won't get WebGL)
                console.warn("MapLibre instance not found, falling back to html2canvas");
                const canvas = await html2canvas(mapContainer, {
                    useCORS: true,
                    allowTaint: true,
                    logging: false,
                    scale: 2
                });
                base64 = canvas.toDataURL("image/png");
            }
        } catch (err)
        {
            console.error("Map capture failed:", err);
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