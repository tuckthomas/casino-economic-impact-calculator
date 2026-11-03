"""Rebuild public county workforce and NE Indiana industry/wage snapshots. No API keys.

Uses the Census table-based summary files and BLS public QCEW/OEWS data.
Retains suppression and sampling margins; never substitutes suppressed employment with zero.
Run with Python 3: python scripts/refresh-public-economy.py
"""
import concurrent.futures
import csv
import datetime
import hashlib
import io
import json
import sys
from pathlib import Path
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'SaveNEIN.Server/Data/PublicEconomy'
CACHE = ROOT / '.cache/public-economy'
OUT.mkdir(parents=True, exist_ok=True)
CACHE.mkdir(parents=True, exist_ok=True)
NOW = datetime.datetime.now(datetime.timezone.utc).isoformat()
MANIFEST = []


def download(url, payload=None):
    key = hashlib.sha256((url + json.dumps(payload)).encode()).hexdigest()
    path = CACHE / key
    if not path.exists() or '--refresh' in sys.argv:
        request = urllib.request.Request(url, data=json.dumps(payload).encode() if payload else None,
                                         headers={'Content-Type': 'application/json'} if payload else {})
        with urllib.request.urlopen(request, timeout=120) as response:
            path.write_bytes(response.read())
    data = path.read_bytes()
    fetched = datetime.datetime.fromtimestamp(path.stat().st_mtime, datetime.timezone.utc).isoformat()
    MANIFEST.append({'url': url, 'requestBody': payload, 'retrievedAtUtc': fetched, 'sha256': hashlib.sha256(data).hexdigest()})
    return data


def estimate(row, table, cell):
    def value(kind):
        raw = row.get(f'{table}_{kind}{cell:03d}', '')
        try:
            number = float(raw)
            return number if number >= 0 else None
        except (ValueError, TypeError):
            return None
    return {'value': value('E'), 'marginOfError': value('M')}


county_data = {}
for table in ['B23025', 'B08007']:
    url = f'https://www2.census.gov/programs-surveys/acs/summary_file/2024/table-based-SF/data/5YRData/acsdt5y2024-{table.lower()}.dat'
    rows = csv.DictReader(io.StringIO(download(url).decode('utf-8-sig')), delimiter='|')
    for row in rows:
        geoid = row['GEO_ID']
        if not geoid.startswith('0500000US'):
            continue
        county = county_data.setdefault(geoid[-5:], {})
        if table == 'B23025':
            for name, cell in [('population16Plus', 1), ('civilianLaborForce', 3), ('employed', 4),
                               ('unemployed', 5), ('notInLaborForce', 7)]:
                county[name] = estimate(row, table, cell)
        else:
            county['residentWorkers'] = estimate(row, table, 1)
            # Use the published all-sex totals and their own margins, not sums of nested sex subtotals.
            for name, cell in [('workInCounty', 3), ('workElsewhereInState', 4), ('workOutsideState', 5)]:
                county[name] = estimate(row, table, cell)
    print('Extracted', table, len(county_data), 'counties', flush=True)
for fips, county in county_data.items():
    values = [county.get(key, {}).get('value') for key in ['residentWorkers','workInCounty','workElsewhereInState','workOutsideState']]
    if all(value is not None for value in values) and values[0] != sum(values[1:]):
        raise ValueError(f'Commuting categories do not reconcile for {fips}')
(OUT / 'acs-2024-counties.json').write_text(json.dumps({'retrievedAtUtc': min(item['retrievedAtUtc'] for item in MANIFEST), 'counties': county_data}, separators=(',', ':')), encoding='utf-8')

COLUMNS = ['area_fips', 'own_code', 'industry_code', 'disclosure_code', 'annual_avg_estabs',
           'annual_avg_emplvl', 'total_annual_wages', 'avg_annual_pay']
SECTORS = {'10','11','21','22','23','31-33','42','44-45','48-49','51','52','53','54','55','56','61','62','71','72','81','721','722'}


def qcew(item):
    fips, year = item
    url = f'https://data.bls.gov/cew/data/api/{year}/a/area/{fips}.csv'
    rows = csv.DictReader(io.StringIO(download(url).decode('utf-8-sig')))
    selected = [{key: row[key] for key in COLUMNS} for row in rows
                if row['own_code'] == '5' and row['industry_code'] in SECTORS and row['size_code'] == '0']
    (OUT / f'qcew-{year}-{fips}.json').write_text(json.dumps({'retrievedAtUtc': next(item['retrievedAtUtc'] for item in MANIFEST if item['url'] == url), 'rows': selected}, separators=(',', ':')), encoding='utf-8')
    return f'QCEW {year} {fips}: {len(selected)} rows'


with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
    for message in pool.map(qcew, [(fips, year) for fips in ['18151','18033','18003'] for year in [2025,2020,2015]] + [('US000',2025)]):
        print(message, flush=True)

occupations = []
for area in ['1800001','0023060']:
    payload = {'areaCode': area, 'industryCode': '000000', 'year': '2025', 'tableSuffix': 'pub', 'userId': '', 'pwd': ''}
    rows = json.loads(download('https://data.bls.gov/OESServices/prefilter/table', payload))
    # All occupations and representative hospitality/security occupations; no county wage inference.
    wanted = {'000000','352014','353011','353031','372012','339032','510000'}
    selected = {}
    for row in rows:
        code = row['occupationCode']
        if code not in wanted or row['datatypeCode'] not in ['01','08','04']:
            continue
        target = selected.setdefault(code, {'code': code, 'name': row['occupationName']})
        try:
            value = float(row['value'].strip().replace(',', ''))
            if value < 0:
                value = None
        except ValueError:
            value = None
        target[{'01':'employment','08':'medianHourly','04':'meanAnnual'}[row['datatypeCode']]] = value
    if not rows:
        raise RuntimeError(f'No published OEWS data for area {area}')
    occupations.append({'areaCode': area, 'areaName': rows[0]['areaName'], 'occupations': list(selected.values())})
(OUT / 'oews-2025-regions.json').write_text(json.dumps({'retrievedAtUtc': min(item['retrievedAtUtc'] for item in MANIFEST if 'OESServices' in item['url']), 'areas': occupations}, separators=(',', ':')), encoding='utf-8')
# Current resident labor-market baseline. Unregistered public BLS API, twelve series in one request.
year = datetime.datetime.now(datetime.timezone.utc).year
payload = {'seriesid': [f'LAUCN{fips}000000000{measure}' for fips in ['18151','18033','18003'] for measure in ['3','4','5','6']],
           'startyear': str(year), 'endyear': str(year)}
response = json.loads(download('https://api.bls.gov/publicAPI/v2/timeseries/data/', payload))
if response.get('status') != 'REQUEST_SUCCEEDED':
    raise RuntimeError('Public BLS LAUS request failed: ' + str(response.get('message')))
series = {item['seriesID']: item['data'] for item in response['Results']['series']}
monthly = {}
for fips in ['18151','18033','18003']:
    by_measure = {}
    for measure in ['3','4','5','6']:
        by_measure[measure] = {row['year'] + '-' + row['period'][1:]: row
            for row in series[f'LAUCN{fips}000000000{measure}'] if row['period'] != 'M13'}
    period = max(set.intersection(*(set(values) for values in by_measure.values())))
    values = {key: float(rows[period]['value']) for key, rows in by_measure.items()}
    if values['6'] != values['4'] + values['5']:
        raise ValueError('LAUS labor force does not reconcile for ' + fips)
    monthly[fips] = {'period': period, 'laborForce': values['6'], 'employed': values['5'], 'unemployed': values['4'],
                     'unemploymentRate': values['3'], 'preliminary': any(note.get('code') == 'P' for rows in by_measure.values() for note in rows[period]['footnotes'])}
(OUT / 'laus-counties.json').write_text(json.dumps({'retrievedAtUtc': next(item['retrievedAtUtc'] for item in MANIFEST if 'publicAPI' in item['url']), 'counties': monthly}, separators=(',', ':')), encoding='utf-8')
(OUT / 'sources.json').write_text(json.dumps({'builtAtUtc': NOW, 'sources': MANIFEST}, indent=2), encoding='utf-8')
print('Public snapshots refreshed; source checksums saved. No paid services or API credentials used.')
