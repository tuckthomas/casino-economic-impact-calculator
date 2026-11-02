window.PdfHelper = {
    captureMapAndGenerate: async function (mapElementId)
    {
        const mapContainer = document.getElementById(mapElementId);
        const mapInstance = window.MapLibreImpactMap?.getMap();
        if (!mapContainer || !mapInstance) throw new Error('Select a location before exporting the map.');
        const previousScroll = { left: window.scrollX, top: window.scrollY, behavior: 'instant' };

        try
        {
            if (!window.html2canvas)
            {
                if (!window.__html2canvasLoad)
                {
                    window.__html2canvasLoad = new Promise((resolve, reject) =>
                    {
                        const script = document.createElement('script');
                        script.src = '/js/lib/html2canvas.min.js';
                        script.onload = resolve;
                        script.onerror = () => reject(new Error('Map export assets could not load.'));
                        document.head.appendChild(script);
                    }).catch(error =>
                    {
                        window.__html2canvasLoad = null;
                        throw error;
                    });
                }
                await window.__html2canvasLoad;
            }

            // Offscreen WebGL frames may be blank. Bring the map into view and
            // wait for its tiles and a fresh frame before reading its canvas.
            mapContainer.scrollIntoView({ block: 'center', behavior: 'instant' });
            mapInstance.resize();
            await new Promise((resolve, reject) =>
            {
                const timer = setTimeout(() =>
                {
                    mapInstance.off('idle', onIdle);
                    reject(new Error('The map is still loading. Please try exporting again.'));
                }, 15000);
                const onIdle = () => { clearTimeout(timer); resolve(); };
                mapInstance.once('idle', onIdle);
                mapInstance.triggerRepaint();
            });

            const mapCanvas = mapInstance.getCanvas();
            const image = new Image();
            await new Promise((resolve, reject) =>
            {
                image.onload = resolve;
                image.onerror = reject;
                image.src = mapCanvas.toDataURL('image/png');
            });
            const composite = document.createElement('canvas');
            composite.width = mapCanvas.width;
            composite.height = mapCanvas.height;
            const context = composite.getContext('2d');
            context.drawImage(image, 0, 0);

            // Capture only markers/popups so a container background cannot
            // paint over the WebGL basemap and impact zones.
            const bounds = mapCanvas.getBoundingClientRect();
            const scaleX = composite.width / bounds.width;
            const scaleY = composite.height / bounds.height;
            for (const overlay of mapContainer.querySelectorAll('.maplibregl-marker, .maplibregl-popup'))
            {
                const rect = overlay.getBoundingClientRect();
                if (!rect.width || !rect.height) continue;
                const rendered = await html2canvas(overlay, { backgroundColor: null, useCORS: true, logging: false });
                context.drawImage(rendered, (rect.left - bounds.left) * scaleX, (rect.top - bounds.top) * scaleY,
                    rect.width * scaleX, rect.height * scaleY);
            }
            return composite.toDataURL('image/png');
        }
        finally
        {
            window.scrollTo(previousScroll);
        }
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
        // Preserve each of the three statements shown by the browser calculator.
        const mainTableData = { headers: ['Statement / item', 'Amount'], rows: [] };
        const statements = [
            ['net-impact-host-table', 'Host Government Fiscal View'],
            ['net-impact-regional-table', 'Regional Revenue vs County Costs'],
            ['net-impact-consolidated-table', 'Consolidated Statement']
        ];
        for (const [id, title] of statements)
        {
            const table = document.getElementById(id)?.querySelector('table');
            if (!table) continue;
            mainTableData.rows.push([title, '']);
            for (const row of table.querySelectorAll('tbody tr'))
            {
                const cells = Array.from(row.querySelectorAll('td'))
                    .map(cell => cell.innerText.trim().replace(/[\n\r]+|info/g, ' '));
                if (cells.length) mainTableData.rows.push([cells[0], cells[1] || '']);
            }
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

                    const toMarkdownText = (node) =>
                    {
                        return Array.from(node.childNodes).map(childNode =>
                        {
                            if (childNode.nodeType === Node.TEXT_NODE)
                            {
                                return childNode.textContent || "";
                            }

                            if (childNode.nodeType !== Node.ELEMENT_NODE)
                            {
                                return "";
                            }

                            const tagName = childNode.tagName;
                            if (tagName === 'UL' || tagName === 'OL')
                            {
                                return "";
                            }

                            const childText = toMarkdownText(childNode);
                            return tagName === 'STRONG' || tagName === 'B'
                                ? `**${childText}**`
                                : childText;
                        }).join('');
                    };

                    const liText = toMarkdownText(clone).replace(/\s+/g, ' ').trim();

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

