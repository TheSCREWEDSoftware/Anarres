#!/usr/bin/env python3
"""Gives AzerothCore's unknown enum labels real names where other sources have them.

Usage: tools/enrich-enum-names.py <TrinityCore 3.3.5 SharedDefines.h> <TrinityCore master SharedDefines.h>

Run after tools/generate-definitions.py. For each value spell-enums-3.3.5a.json labels unknown or unused, in order:
  1. AzerothCore's own constant name, when it isn't UNK or UNUSED;
  2. TrinityCore 3.3.5's name and notes;
  3. for attribute bits only, TrinityCore master's name, marked as unconfirmed for 3.3.5a (attribute bits kept their
     meaning in later clients; other flag lists, such as aura interrupts, were renumbered, so master isn't used).
Attribute bits also say whether the 3.3.5a client reads them (tools/client-attribute-bits.txt).
"""
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, '..', 'src', 'Anarres.Core', 'Definitions', 'spell-enums-3.3.5a.json')
ATTRIBUTES = ['Attributes', 'AttributesEx', 'AttributesEx2', 'AttributesEx3', 'AttributesEx4', 'AttributesEx5',
              'AttributesEx6', 'AttributesEx7']
CLIENT_FIELDS = ['m_attributes', 'm_attributesEx', 'm_attributesExB', 'm_attributesExC', 'm_attributesExD',
                 'm_attributesExE', 'm_attributesExF', 'm_attributesExG']
# Our enum: the TrinityCore enum, its prefix, and whether master may be used
SOURCES = {key: ('SpellAttr%d' % i, 'SPELL_ATTR%d_' % i, True) for i, key in enumerate(ATTRIBUTES)}
SOURCES.update({'Target': ('Targets', 'TARGET_', False), 'AuraState': ('AuraStateType', 'AURA_STATE_', False),
                'CastTargetFlags': ('SpellCastTargetFlags', 'TARGET_FLAG_', False),
                'InterruptFlags': ('SpellInterruptFlags', 'SPELL_INTERRUPT_FLAG_', False),
                'AuraInterruptFlags': ('SpellAuraInterruptFlags', 'AURA_INTERRUPT_FLAG_', False),
                'Dispel': ('DispelType', 'DISPEL_', False), 'SpellFamily': ('SpellFamilyNames', 'SPELLFAMILY_', False)})


def unknown(name, label):
    return bool(re.search(r'UNK|UNUSED', name)) or bool(re.search(r'unknown|unused', label, re.I))


def meaningful(name):
    return not re.search(r'(^|_)(UNK\d*|UNUSED\d*|UNKNOWN\d*)(_|$)', name)


def nice(name, prefix):
    if name.startswith(prefix):
        name = name[len(prefix):]
    words = [w.capitalize() for w in name.split('_') if not w.isdigit()]
    fixes = {'Pvp': 'PvP', 'Ui': 'UI', 'Dont': "Don't", 'Cant': "Can't", 'Aoe': 'AoE', 'Los': 'LoS', 'Gcd': 'GCD',
             'Npc': 'NPC', 'Dot': 'DoT', 'Hot': 'HoT'}
    return ' '.join(fixes.get(w, w) for w in words)


def parse(path, enum):
    text = open(path, encoding='utf-8', errors='replace').read()
    m = re.search(r'^enum (?:class )?' + enum + r'\b[^{;]*\{(.*?)^\};', text, re.S | re.M)
    values = {}
    if not m:
        return values
    for line in m.group(1).split('\n'):
        e = re.match(r'\s*([A-Z][A-Z0-9_]*)\s*=\s*(0x[0-9A-Fa-f]+|\d+)[uUlL]*\s*,?\s*(?://\s*(.*))?$', line)
        if not e:
            continue
        name, value, comment = e.group(1), int(e.group(2), 0), (e.group(3) or '').strip()
        title, desc = '', re.sub(r'^\d+\s+', '', comment)
        t = re.search(r'TITLE\s+(.*?)(?:\s+DESCRIPTION\s+(.*))?$', comment)
        if t:
            title, desc = re.sub(r'@Attr\d+$', '', t.group(1)).strip(), (t.group(2) or '').strip()
        values.setdefault(value, (name, title, desc))
    return values


def client_bits():
    bits = {}
    for line in open(os.path.join(HERE, 'client-attribute-bits.txt')):
        if line.startswith('#') or not line.strip():
            continue
        field, mask = line.split()
        bits[field] = bits.get(field, 0) | int(mask, 16)
    return bits


def relabel(value, prefix, old, new):
    v, name, label = value['Value'], value['Name'], value['Label']
    if not unknown(name, label):
        return False
    if meaningful(name):
        value['Label'] = nice(name, prefix)
    elif v in old and meaningful(old[v][0]) and not unknown(old[v][0], old[v][1]):
        value['Label'] = re.sub(r'\s+\d+$', '', old[v][1]) or nice(old[v][0], prefix)
        value['Description'] = (old[v][2] + ' ' if old[v][2] else '') + '(TrinityCore 3.3.5 names it ' + \
            old[v][0] + '.)'
    elif v in new and meaningful(new[v][0]) and not unknown(new[v][0], new[v][1]):
        value['Label'] = (new[v][1] or nice(new[v][0], prefix)) + ' (unconfirmed)'
        value['Description'] = (new[v][2] + ' ' if new[v][2] else '') + '(TrinityCore names it ' + new[v][0] + \
            ' from later clients; not confirmed for 3.3.5a.)'
    return value['Label'] != label


def main():
    tc335, master = sys.argv[1], sys.argv[2]
    data = json.load(open(OUT))
    reads = client_bits()
    changed = 0
    for key, (enum, prefix, use_master) in SOURCES.items():
        if key not in data['Enums']:
            continue
        old = parse(tc335, enum)
        new = parse(master, enum) if use_master else {}
        for value in data['Enums'][key]['Values']:
            if relabel(value, prefix, old, new):
                changed += 1
            if key in ATTRIBUTES:
                field = CLIENT_FIELDS[ATTRIBUTES.index(key)]
                note = 'The 3.3.5a client reads this bit.' if reads.get(field, 0) & value['Value'] \
                    else 'The 3.3.5a client never reads this bit; only the server can act on it.'
                if note not in value['Description']:
                    value['Description'] = (value['Description'].rstrip() + ' ' + note).strip()
    with open(OUT, 'w', newline='\n') as stream:
        json.dump(data, stream, indent=2)
        stream.write('\n')
    print('relabelled', changed, file=sys.stderr)


main()
