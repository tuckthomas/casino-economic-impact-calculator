window.Legislators = (function() {
    let currentLegislators = { senate: null, house: null };
    let legislatorData = null;

    async function loadLegislatorData() {
        try {
            const response = await fetch('./data/legislators.json');
            legislatorData = await response.json();
        } catch (e) {
            console.error("Could not load legislator data", e);
            legislatorData = { senate: {}, house: {} }; 
        }
    }

    function updateRepCard(type, district) {
        // Clean district number
        const distNum = parseInt(district).toString();
        
        // Map type to JSON keys
        const dataKey = type === 'senate' ? 'state_senate' : 'state_house';
        
        // Map type to DOM ID prefix
        const domPrefix = type === 'senate' ? 'senator' : type;

        // Look up name
        let name = "Unknown";
        let email = "";

        if (legislatorData && legislatorData[dataKey] && legislatorData[dataKey][distNum]) {
            const rep = legislatorData[dataKey][distNum];
            name = rep.name + " (" + rep.party + ")";
            email = rep.email;
        } else {
            name = `District ${distNum} Representative`;
            email = `${type === 'senate' ? 's' : 'h'}${distNum}@iga.in.gov`;
        }

        // Store for template use
        currentLegislators[type] = { name, email, district: distNum };

        // Update DOM
        const nameEl = document.getElementById(`${domPrefix}-name`);
        const distEl = document.getElementById(`${domPrefix}-district`);
        const emailBtn = document.getElementById(`${domPrefix}-email-btn`);

        if (nameEl) nameEl.textContent = name;
        if (distEl) distEl.textContent = `District ${distNum}`;
        if (emailBtn) emailBtn.href = `mailto:${email}`;
    }

    window.selectTemplate = function(topic) {
        if (!currentLegislators.senate && !currentLegislators.house) {
            alert("Please find your representatives first.");
            return;
        }

        const subject = "Vote NO on the Casino - Protect Our Community";
        let body = "";

        if (topic === 'economic') {
            body = `Dear Representative,\n\nI am writing to urge you to OPPOSE the proposed casino expansion in Northeast Indiana. The economic promises ignore the 'Substitution Effect'—money spent at the casino is money diverted from existing local businesses. We cannot afford a project that extracts wealth from our community and leaves us with the social costs of bankruptcy and addiction.\n\nPlease protect our local economy and vote NO.`;
        } else if (topic === 'crime') {
            body = `Dear Representative,\n\nI am deeply concerned about the public safety risks associated with the proposed casino. Statistics show that casinos bring increased crime, human trafficking, and domestic violence to host communities. Local families deserve safe neighborhoods, not a hub for predatory gambling.\n\nPlease prioritize our safety and vote NO.`;
        } else if (topic === 'referendum') {
            body = `Dear Representative,\n\nI believe that a decision with such permanent consequences for Northeast Indiana requires a public referendum. Bypassing the voters on an issue that fundamentally changes our region's character is unacceptable.\n\nPlease demand a public vote or vote NO on the casino measure.`;
        }

        const emails = [currentLegislators.senate?.email, currentLegislators.house?.email].filter(Boolean).join(',');
        window.location.href = `mailto:${emails}?subject=${encodeURIComponent(subject)}&body=${encodeURIComponent(body)}`;
    }

    // Expose updateRepCard globally for the geocoder callback
    window.updateRepCard = updateRepCard;

    function init() {
        loadLegislatorData();
        // Geocoder initialization is handled in map.js now?
        // Wait, the geocoder was in the Map script block in original index.html but moved elements to 'rep-geocoder-container'.
        // In my modular map.js, I need to ensure that geocoder logic exists and targets 'rep-geocoder-container'.
    }

    return { init: init };
})();
