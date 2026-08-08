window.SliderInputGroupLogic = (function() {
    function init(container, totalDisplayId) {
        const totalDisplay = document.getElementById(totalDisplayId);
        if (!container || !totalDisplay) {
            console.error("SliderInputGroup: Missing container or total display element.");
            return;
        }

        if (container.dataset.groupInitialized === "true") return;
        container.dataset.groupInitialized = "true";

        const updateListeners = () => {
            const inputs = container.querySelectorAll('input[type="range"]');
            
            const calculateTotal = () => {
                let sum = 0;
                inputs.forEach(input => {
                    const val = parseFloat(input.value);
                    if (!isNaN(val)) sum += val;
                });
                totalDisplay.textContent = '$' + sum.toLocaleString(undefined, { minimumFractionDigits: 0, maximumFractionDigits: 0 });
            };

            inputs.forEach(input => {
                if (!input.dataset.groupBound) {
                    input.addEventListener('input', calculateTotal);
                    input.dataset.groupBound = "true";
                }
            });

            calculateTotal();
        };

        updateListeners();

        const observer = new MutationObserver((mutations) => {
            updateListeners();
        });
        
        observer.observe(container, { childList: true, subtree: true });
    }

    return { init: init };
})();