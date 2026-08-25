"use strict";

const registrations = new Map();
const lightIds = [
    "lightbar-red",
    "lightbar-red-glow",
    "lightbar-blue",
    "lightbar-blue-glow"
];

function getSvgRoot(registration) {
    return registration.vehicle.querySelector("svg");
}

async function mountVehicle(registration) {
    const existingSvg = getSvgRoot(registration);
    if (existingSvg) return existingSvg;

    const source = registration.vehicle.dataset.svgSrc;
    if (!source) return null;

    const response = await fetch(source, {
        cache: "force-cache",
        signal: registration.abortController.signal
    });
    if (!response.ok) {
        throw new Error(`Unable to load police vehicle SVG (${response.status}).`);
    }

    const svgText = await response.text();
    const svgDocument = new DOMParser().parseFromString(svgText, "image/svg+xml");
    if (svgDocument.querySelector("parsererror")) {
        throw new Error("Unable to parse police vehicle SVG.");
    }

    const svg = document.importNode(svgDocument.documentElement, true);
    svg.style.width = "100%";
    svg.style.height = "100%";
    svg.style.display = "block";
    registration.vehicle.replaceChildren(svg);
    return svg;
}

function stopLights(registration) {
    registration.lightAnimations.forEach(animation => animation.cancel());
    registration.lightAnimations = [];

    const svg = getSvgRoot(registration);
    if (!svg) return;

    lightIds.forEach(id => {
        const element = svg.querySelector(`#${id}`);
        if (element) element.setAttribute("opacity", "0");
    });
}

function startLights(registration) {
    const svg = getSvgRoot(registration);
    if (!svg) return;

    const vehicleBody = svg.querySelector("#vehicle-body");
    if (vehicleBody) vehicleBody.setAttribute("opacity", "1");

    const redElements = [
        svg.querySelector("#lightbar-red"),
        svg.querySelector("#lightbar-red-glow")
    ].filter(Boolean);
    const blueElements = [
        svg.querySelector("#lightbar-blue"),
        svg.querySelector("#lightbar-blue-glow")
    ].filter(Boolean);

    const redFrames = [
        { opacity: 1, offset: 0 },
        { opacity: 1, offset: 0.36 },
        { opacity: 0, offset: 0.46 },
        { opacity: 0, offset: 1 }
    ];
    const blueFrames = [
        { opacity: 0, offset: 0 },
        { opacity: 0, offset: 0.46 },
        { opacity: 1, offset: 0.56 },
        { opacity: 1, offset: 0.9 },
        { opacity: 0, offset: 1 }
    ];
    const timing = { duration: 520, iterations: Infinity, easing: "linear" };

    registration.lightAnimations = [
        ...redElements.map(element => element.animate(redFrames, timing)),
        ...blueElements.map(element => element.animate(blueFrames, timing))
    ];
}

async function activate(registration) {
    if (registration.triggered) return;
    registration.triggered = true;
    registration.observer.disconnect();

    try {
        await mountVehicle(registration);
    } catch (error) {
        if (error.name !== "AbortError") console.error(error);
        return;
    }

    if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) {
        registration.stage.classList.add("is-reduced-motion");
        return;
    }

    startLights(registration);
    registration.onAnimationEnd = event => {
        if (event.target !== registration.vehicle) return;
        registration.completed = true;
        stopLights(registration);
        registration.stage.classList.remove("is-driving");
        registration.stage.classList.add("is-complete");
    };
    registration.vehicle.addEventListener("animationend", registration.onAnimationEnd, { once: true });

    requestAnimationFrame(() => registration.stage.classList.add("is-driving"));
}

export function init(stageId) {
    dispose(stageId);

    const stage = document.getElementById(stageId);
    const vehicle = stage?.querySelector(".crime-police-driveby__vehicle");
    if (!stage || !vehicle) return;

    const registration = {
        stage,
        vehicle,
        observer: null,
        abortController: new AbortController(),
        lightAnimations: [],
        onAnimationEnd: null,
        triggered: false,
        completed: false
    };

    registration.observer = new IntersectionObserver(entries => {
        if (entries.some(entry => entry.isIntersecting)) void activate(registration);
    }, {
        threshold: 0.2,
        rootMargin: "0px 0px -10% 0px"
    });

    registrations.set(stageId, registration);
    registration.observer.observe(stage);
}

export function dispose(stageId) {
    const registration = registrations.get(stageId);
    if (!registration) return;

    registration.observer.disconnect();
    registration.abortController.abort();
    stopLights(registration);
    if (registration.onAnimationEnd) {
        registration.vehicle.removeEventListener("animationend", registration.onAnimationEnd);
    }
    registrations.delete(stageId);
}
