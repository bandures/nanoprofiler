import helpers

def verify(tp):
    helpers.verify_track_exists(tp, "MyGroup")
    helpers.verify_slice_exists(tp, "RootScope")
    helpers.verify_slice_exists(tp, "MyActivity")

if __name__ == "__main__":
    helpers.main(verify)
