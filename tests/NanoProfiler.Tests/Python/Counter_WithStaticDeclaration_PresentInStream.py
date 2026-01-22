import helpers

def verify(tp):
    helpers.verify_counter_track_exists(tp, "MyCounter")
    helpers.verify_counter_values(tp, "MyCounter", [1.0, 2.0, 5.0])

if __name__ == "__main__":
    helpers.main(verify)
