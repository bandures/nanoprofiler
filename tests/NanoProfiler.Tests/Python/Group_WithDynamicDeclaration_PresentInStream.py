import helpers

def verify(tp):
    helpers.verify_track_exists(tp, "MyGroup")

if __name__ == "__main__":
    helpers.main(verify)
