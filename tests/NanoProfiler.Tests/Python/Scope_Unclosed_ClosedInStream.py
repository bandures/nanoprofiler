import helpers

def verify(tp):
    helpers.verify_slice_closed(tp, "UnclosedScope")

if __name__ == "__main__":
    helpers.main(verify)
