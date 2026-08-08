window.SubstitutionEffect = (function() {
    function init() {
        const commonOptions = {
            responsive: true,
            maintainAspectRatio: true,
            plugins: {
                legend: { display: false },
                tooltip: {
                    callbacks: {
                        label: function (context)
                        {
                            return context.label + ': $' + context.raw;
                        }
                    },
                    backgroundColor: 'rgba(15, 23, 42, 0.95)', // Dark tooltips
                    titleColor: '#fff',
                    bodyColor: '#cbd5e1',
                    borderColor: 'rgba(148, 163, 184, 0.5)',
                    borderWidth: 1,
                    padding: 10
                }
            },
            layout: { padding: 10 }
        };

        // Data
        const labels = ['Retail', 'Dining', 'Local Ent.', 'Savings', 'Casino'];
        const bgColors = ['#3b82f6', '#8b5cf6', '#10b981', '#f97316', '#ef4444'];

        // Chart 1: Before ($500)
        const canvasBefore = document.getElementById('chartBefore');
        if(canvasBefore) {
            const ctxBefore = canvasBefore.getContext('2d');
            new Chart(ctxBefore, {
                type: 'doughnut',
                data: {
                    labels: ['Retail', 'Dining', 'Local Ent.', 'Savings'],
                    datasets: [{
                        data: [150, 150, 100, 100],
                        backgroundColor: bgColors.slice(0, 4),
                        borderWidth: 0,
                        hoverOffset: 10
                    }]
                },
                options: commonOptions,
                plugins: [{
                    id: 'centerText',
                    beforeDraw: function (chart)
                    {
                        var width = chart.width,
                            height = chart.height,
                            ctx = chart.ctx;
                        ctx.restore();
                        var fontSize = (height / 114).toFixed(2);
                        ctx.font = "bold " + fontSize + "em sans-serif";
                        ctx.textBaseline = "middle";
                        ctx.fillStyle = "#ffffff"; // White Text
                        var text = "$500",
                            textX = Math.round((width - ctx.measureText(text).width) / 2),
                            textY = height / 2;
                        ctx.fillText(text, textX, textY);
                        ctx.save();
                    }
                }]
            });
        }

        // Chart 2: After ($500)
        const canvasAfter = document.getElementById('chartAfter');
        if(canvasAfter) {
            const ctxAfter = canvasAfter.getContext('2d');
            new Chart(ctxAfter, {
                type: 'doughnut',
                data: {
                    labels: labels,
                    datasets: [{
                        data: [100, 100, 75, 75, 150],
                        backgroundColor: bgColors,
                        borderWidth: 0,
                        hoverOffset: 10
                    }]
                },
                options: commonOptions,
                plugins: [{
                    id: 'centerText',
                    beforeDraw: function (chart)
                    {
                        var width = chart.width,
                            height = chart.height,
                            ctx = chart.ctx;
                        ctx.restore();
                        var fontSize = (height / 114).toFixed(2);
                        ctx.font = "bold " + fontSize + "em sans-serif";
                        ctx.textBaseline = "middle";
                        ctx.fillStyle = "#ffffff"; // White text
                        var text = "$500",
                            textX = Math.round((width - ctx.measureText(text).width) / 2),
                            textY = height / 2;
                        ctx.fillText(text, textX, textY);
                        ctx.save();
                    }
                }]
            });
        }
    }

    return { init: init };
})();
