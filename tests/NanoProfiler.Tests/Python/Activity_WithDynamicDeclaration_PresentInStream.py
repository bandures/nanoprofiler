import helpers

def verify(tp):
    helpers.verify_slice_exists(tp, "MyActivity")

if __name__ == "__main__":
    helpers.main(verify)
