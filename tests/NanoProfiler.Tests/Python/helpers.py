from perfetto.trace_processor import TraceProcessor
import sys

def check(condition, msg):
    if not condition:
        print(f"FAIL: {msg}")
        sys.exit(1)

def load_trace(trace_path):
    print(f"Loading trace: {trace_path}")
    return TraceProcessor(trace=trace_path)

def verify_slice_exists(tp, name):
    print(f"Verifying slice '{name}'...")
    query = f'SELECT name FROM slice WHERE name = "{name}"'
    rows = list(tp.query(query))
    if len(rows) == 0:
        # Debug: print all slices
        all_rows = list(tp.query('SELECT name FROM slice'))
        print(f"All slices: {[r.name for r in all_rows]}")
    check(len(rows) > 0, f"Slice '{name}' not found")
    print(f"Found slice: {name}")

def verify_slice_closed(tp, name):
    print(f"Verifying slice '{name}' is closed...")
    query = f'SELECT dur FROM slice WHERE name = "{name}"'
    rows = list(tp.query(query))
    if len(rows) == 0:
        check(False, f"Slice '{name}' not found")
    
    dur = rows[0].dur
    check(dur != -1 and dur >= 0, f"Slice '{name}' is not closed properly (dur={dur})")
    print(f"Slice '{name}' is closed (dur={dur})")

def verify_slice_count_prefix(tp, prefix, expected_count):
    print(f"Verifying slice count for prefix '{prefix}'...")
    query = f'SELECT name FROM slice WHERE name LIKE "{prefix}%"'
    rows = list(tp.query(query))
    count = len(rows)
    check(count == expected_count, f"Expected {expected_count} slices with prefix '{prefix}', found {count}")
    print(f"Found {count} slices with prefix '{prefix}'")

def verify_counter_values(tp, name, expected_values):
    print(f"Verifying counter '{name}' values...")
    query = f'''
        SELECT c.value 
        FROM counter c 
        JOIN counter_track t ON c.track_id = t.id 
        WHERE t.name LIKE "{name}%"
    '''
    rows = list(tp.query(query))
    vals = [row.value for row in rows]
    print(f"Actual values: {vals}")
    for v in expected_values:
        check(v in vals, f"Expected value {v} not found in {vals}")

def verify_counter_track_exists(tp, name):
    print(f"Verifying counter track '{name}'...")
    query = f'SELECT name FROM counter_track WHERE name LIKE "{name}%"'
    rows = list(tp.query(query))
    if len(rows) == 0:
        # Debug: print all counter tracks
        all_rows = list(tp.query('SELECT name FROM counter_track'))
        print(f"All counter tracks: {[r.name for r in all_rows]}")
    check(len(rows) > 0, f"Counter track '{name}' not found")
    print(f"Found counter track: {rows[0].name}")

def verify_instant_on_track(tp, name, track_name):
    print(f"Verifying instant '{name}' on track '{track_name}'...")
    query = f'''
        SELECT s.dur
        FROM slice s
        JOIN track t ON s.track_id = t.id
        WHERE s.name = "{name}" AND t.name = "{track_name}"
    '''
    rows = list(tp.query(query))
    if len(rows) == 0:
        all_rows = list(tp.query('SELECT s.name, s.dur, t.name AS tname FROM slice s JOIN track t ON s.track_id = t.id'))
        print(f"All slices: {[(r.name, r.dur, r.tname) for r in all_rows]}")
    check(len(rows) > 0, f"Instant '{name}' not found on track '{track_name}'")
    for row in rows:
        check(row.dur == 0, f"Instant '{name}' must have no duration (dur={row.dur})")
    print(f"Found {len(rows)} instant(s) '{name}' on track '{track_name}'")

def verify_track_exists(tp, name):
    print(f"Verifying track '{name}'...")
    query = f'SELECT name FROM track WHERE name = "{name}"'
    rows = list(tp.query(query))
    if len(rows) == 0:
        # Debug: print all tracks
        query_all = 'SELECT id, type, name FROM track'
        all_rows = list(tp.query(query_all))
        print(f"All tracks: {[(r.id, r.type, r.name) for r in all_rows]}")
    check(len(rows) > 0, f"Track '{name}' not found")
    print(f"Found track: {name}")

def main(verify_func):
    if len(sys.argv) < 2:
        print("Usage: python <script.py> <trace_path>")
        sys.exit(1)
    
    trace_path = sys.argv[1]
    tp = load_trace(trace_path)
    verify_func(tp)
    print("SUCCESS: Verification passed.")