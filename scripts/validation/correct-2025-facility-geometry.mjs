import { createHash } from 'node:crypto';
import { existsSync, readFileSync, writeFileSync } from 'node:fs';

// A new derived capture; never modify the sealed inputs used in earlier searches.
const [basePath, geocodePath, addressPdfPath, outputPath, auditPath] = process.argv.slice(2);
if (!auditPath || existsSync(outputPath) || existsSync(auditPath)) {
    throw new Error('Supply base bundle, retained Aurora geocode, historical address PDF, and two new output paths.');
}
const digest = bytes => createHash('sha256').update(bytes).digest('hex');
const baseBytes = readFileSync(basePath);
const sourceBundleSha256 = digest(baseBytes);
if (sourceBundleSha256 !== '3320f1cdbb6a3972f11f500472b217123943c77550bdb96398c53443c9ff7dd9') {
    throw new Error('Historical correction requires the frozen 102-facility border-supply capture.');
}
const sourcePdfSha256 = digest(readFileSync(addressPdfPath));
if (sourcePdfSha256 !== '8018fe9ef2a6756106fad54b402a81e36ae53549fd8df046877afea43dccbde4') {
    throw new Error('Historical address source differs from the inspected IGB 2024 annual report.');
}
const geoBytes = readFileSync(geocodePath);
const geo = JSON.parse(geoBytes).candidates?.[0];
if (!geo || geo.score < 90 || geo.address !== '1 W New York St, Aurora, Illinois, 60506' ||
    !Number.isFinite(geo.location?.y) || !Number.isFinite(geo.location?.x) ||
    geo.location.y < 41.75 || geo.location.y > 41.77 || geo.location.x < -88.32 || geo.location.x > -88.30) {
    throw new Error('Aurora historical geocode failed address/coordinate checks.');
}
const bundle = JSON.parse(baseBytes);
const auroras = bundle.Facilities.Rows.filter(row => row.StableVenueId === 'USA-IL-IGB-hollywood-casino-aurora');
if (auroras.length !== 1) throw new Error('Expected exactly one Aurora competitor.');
const aurora = auroras[0];
const previousCoordinates = { latitude: aurora.Latitude, longitude: aurora.Longitude };
aurora.Latitude = geo.location.y;
aurora.Longitude = geo.location.x;
const addressSourceUrl = 'https://igb.illinois.gov/content/dam/soi/en/web/igb/documents/reports/annual-reports/igb-annual-reports/2024-igb-annual-report.pdf';
const relocationSourceUrl = 'https://pennnationalgaming.gcs-web.com/news-releases/news-release-details/penn-entertainment-celebrates-grand-opening-hollywood-casino-and';
aurora.Notes += ` Historical-geometry correction: calendar 2025 uses 1 W. New York St., Aurora, IL 60506 (${addressSourceUrl}). The current Farnsworth site opened June 24, 2026 (${relocationSourceUrl}). Geocode ${geo.address}, score ${geo.score}; earlier licensed-address coordinates remain in the source bundle.`;
const paRows = bundle.Facilities.Rows.filter(row => row.State === 'PA');
if (paRows.length !== 17) throw new Error('Expected seventeen retained Pennsylvania profiles.');
for (const row of paRows) {
    row.PokerTableCount = null;
    row.HasPoker = null;
    row.Notes += ' Nonbanked table category alone does not establish poker presence/count; those fields remain unknown.';
}
const transformVersion = 'known-border-supply-temporal-v3';
const checksum = digest(JSON.stringify({ transformVersion, sourceBundleSha256, sourcePdfSha256,
    sourceGeocodeSha256: digest(geoBytes), rows: bundle.Facilities.Rows }));
bundle.Facilities.ContentChecksum = checksum;
bundle.Facilities.TransformVersion = transformVersion;
bundle.Facilities.Source.ContentHash = checksum;
bundle.Facilities.Source.Notes += ` Derived capture ${transformVersion}; base bytes ${sourceBundleSha256}; IGB address PDF ${sourcePdfSha256}; Aurora geocode ${digest(geoBytes)}. Pennsylvania nonbanked counts are not relabeled poker. Comparable revenue observations are unchanged.`;
bundle.Facilities.Warnings.push('Hollywood Joliet relocated August 11, 2025. Its static end-period site still requires effective-dated annual competition treatment; this capture is not release-accepted.');
const audit = { transformVersion, sourceBundleSha256, sourcePdfSha256, addressSourceUrl, relocationSourceUrl,
    sourceGeocodeSha256: digest(geoBytes), geocodeAddress: geo.address, geocodeScore: geo.score, previousCoordinates,
    correctedCoordinates: { latitude: geo.location.y, longitude: geo.location.x },
    facilityContentChecksum: checksum, facilityRows: bundle.Facilities.Rows.length,
    performanceSourceChecksumUnchanged: bundle.Performance.ContentChecksum,
    scope: 'Historical Aurora coordinate and conservative PA category correction only. Joliet temporal sites, Wisconsin capacities, other competitors and input/fiscal acceptance remain incomplete.',
    releaseAccepted: false };
writeFileSync(outputPath, JSON.stringify(bundle, null, 2) + '\n');
writeFileSync(auditPath, JSON.stringify(audit, null, 2) + '\n');
console.log(JSON.stringify(audit));
