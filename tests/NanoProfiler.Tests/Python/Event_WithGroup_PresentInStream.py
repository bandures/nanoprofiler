import helpers

def verify(tp):
    helpers.verify_track_exists(tp, "MyGroup")
    # Interned name, no parameters
    helpers.verify_instant_on_track(tp, "GroupEvent", "MyGroup")
    # Inline name, parameters are appended to it
    helpers.verify_instant_on_track(tp, "GroupEvent[42][state]", "MyGroup")

if __name__ == "__main__":
    helpers.main(verify)
