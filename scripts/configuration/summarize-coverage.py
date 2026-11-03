#!/usr/bin/env python3
"""Union Cobertura line hits across the configured core and adapter runners."""
import json
import os
import sys
import xml.etree.ElementTree as ET
from collections import defaultdict

files = defaultdict(dict)
for report in sys.argv[1:]:
    root = ET.parse(report).getroot()
    sources = [item.text for item in root.findall('./sources/source')]
    for package in root.findall('.//package'):
        if package.get('name') not in ('Morphir.Configuration', 'Morphir.Configuration.IO'):
            continue
        for item in package.findall('./classes/class'):
            filename = item.get('filename')
            if not os.path.isabs(filename):
                filename = os.path.join(sources[0], filename)
            if '/obj/' in filename.replace('\\', '/'):
                continue
            for line in item.findall('./lines/line'):
                number = int(line.get('number'))
                files[filename][number] = max(files[filename].get(number, 0), int(line.get('hits')))

result = {}
for module in ('Morphir.Configuration', 'Morphir.Configuration.IO'):
    selected = {name: lines for name, lines in files.items()
                if '/src/' + module + '/' in name.replace('\\', '/')}
    total = sum(len(lines) for lines in selected.values())
    covered = sum(hits > 0 for lines in selected.values() for hits in lines.values())
    result[module] = {'covered': covered, 'total': total,
                      'percent': round(100 * covered / total, 2) if total else 0}
print(json.dumps(result, indent=2))
